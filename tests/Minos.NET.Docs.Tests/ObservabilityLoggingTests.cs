using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Minos.Docs.Tests;

public sealed partial class ObservabilityLoggingTests
{
    private const string Page = "observability.md";
    private const string SecretKey = "SECRET-KEY-VALUE";

    private const string UrgentResponse = """
        {
          "model": "jev-1.13.0",
          "answers": { "is_urgent": { "type": "noul", "noul": 0.93 } },
          "usage": { "input_tokens": 41, "output_tokens": 3 }
        }
        """;

    private const string ModelsResponse = """
        { "models": [ { "name": "jev-latest", "description": "The most recent stable release.", "release_date": "2026-09-15" } ] }
        """;

    private static DecisionClientOptions Options(int maxRetries = 0)
    {
        var options = ScriptedDecision.Quick(maxRetries);
        options.ApiKey = SecretKey;
        return options;
    }

    private static async Task<IReadOnlyList<FakeLogRecord>> LogAsync(DecisionClientOptions options, params Reply[] script)
    {
        var (http, _) = ScriptedDecision.Http(options, script);
        using (http)
        {
            return await ObservedLogging.EvaluateAndCollectAsync(http, options, CancellationToken.None);
        }
    }

    private static string Field(FakeLogRecord record, string name)
    {
        var fields = new List<string>();
        foreach (var pair in record.StructuredState!)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal))
            {
                fields.Add(pair.Value ?? "(null)");
            }
        }

        return Only.Of(fields);
    }

    private static string Everything(FakeLogRecord record)
    {
        var text = new StringBuilder(record.Message).Append(record.Exception);
        foreach (var pair in record.StructuredState!)
        {
            text.Append(pair.Value);
        }

        return text.ToString();
    }

    [Fact]
    public async Task ASuccessfulEvaluation_LogsOneDebugEvent_WithItsFields()
    {
        var records = await LogAsync(Options(), Reply.Ok(UrgentResponse));

        var record = Only.Of(records);
        Assert.Equal(1001, record.Id.Id);
        Assert.Equal("EvaluationSucceeded", record.Id.Name);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.Equal("Minos.DecisionClient", record.Category);
        Assert.Equal("evaluate-set", Field(record, "Operation"));
        Assert.Equal("jev-latest", Field(record, "Model"));
        Assert.Equal("typesafe", Field(record, "Provider"));
        Assert.Equal("1", Field(record, "QuestionCount"));
        Assert.Matches(@"^Minos evaluate-set on jev-latest via typesafe succeeded: 1 questions in [0-9.,]+ ms\.$", record.Message);
    }

    [Fact]
    public async Task ARetriedEvaluation_LogsEachRetriedAttempt_ThenItsOutcome()
    {
        var records = await LogAsync(Options(2), Reply.Error(503), Reply.Ok(UrgentResponse));

        Assert.Equal([1003, 1001], records.Select(record => record.Id.Id));
        Assert.Equal(LogLevel.Warning, records[0].Level);
        Assert.Equal("1", Field(records[0], "Attempt"));
        Assert.Equal("Overloaded", Field(records[0], "ErrorKind"));
        Assert.Equal("503", Field(records[0], "StatusCode"));
    }

    [Fact]
    public async Task AFailureAfterTheRetries_IsLoggedOnce_AndTheLastAttemptIsNotLoggedAsARetry()
    {
        var records = await LogAsync(Options(1), Reply.Error(500));

        Assert.Equal([1003, 1002], records.Select(record => record.Id.Id));
        Assert.Equal(LogLevel.Warning, records[1].Level);
        Assert.Equal("Server", Field(records[1], "ErrorKind"));
        Assert.Equal("500", Field(records[1], "StatusCode"));
        Assert.Equal("evaluate-set", Field(records[1], "Operation"));
    }

    [Fact]
    public async Task ANetworkFailure_LogsAFixedMessage_NotTheTransportsText()
    {
        var records = await LogAsync(Options(), Reply.Refused);

        var record = Only.Of(records);
        Assert.Equal(1002, record.Id.Id);
        Assert.Equal("Network", Field(record, "ErrorKind"));
        Assert.Equal("The request could not be sent.", Field(record, "ErrorMessage"));
        Assert.Contains("status (null)", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", record.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnreadableResponse_LogsAFixedMessage_NotWhatTheServerSaid()
    {
        var records = await LogAsync(Options(), Reply.Ok("this is not json"));

        var record = Only.Of(records);
        Assert.Equal(1002, record.Id.Id);
        Assert.Equal("InvalidResponse", Field(record, "ErrorKind"));
        Assert.Equal("The response could not be read.", Field(record, "ErrorMessage"));
    }

    [Fact]
    public async Task OtherFailures_LogTheErrorsOwnMessage()
    {
        var records = await LogAsync(Options(), Reply.Error(401));

        var record = Only.Of(records);
        Assert.Equal("Unauthorized", Field(record, "ErrorKind"));
        Assert.Equal("401", Field(record, "StatusCode"));
        Assert.False(string.IsNullOrWhiteSpace(Field(record, "ErrorMessage")));
    }

    [Fact]
    public async Task NoRecord_CarriesTheState_TheKey_OrTheServersErrorBody()
    {
        var detail = "{\"detail\":\"SECRET-ERROR-BODY\"}";
        var scripts = new[] { Reply.Ok(UrgentResponse), Reply.Error(422, detail), Reply.Error(401, detail), Reply.Refused, Reply.Ok("SECRET-ERROR-BODY not json") };
        var text = new StringBuilder();
        foreach (var script in scripts)
        {
            var records = await LogAsync(Options(), script);

            Assert.NotEmpty(records);
            foreach (var record in records)
            {
                text.Append(Everything(record));
            }
        }

        Assert.DoesNotContain("SECRET-ERROR-BODY", text.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, text.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("The server is down", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModelListing_LogsItsOwnEvents()
    {
        var options = Options();
        var (http, _) = ScriptedDecision.Http(options, Reply.Ok(ModelsResponse));
        using (http)
        using (var provider = new FakeLoggerProvider())
        using (var loggers = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Debug)))
        using (var client = new DecisionClient(http, options, loggers))
        {
            await client.ListModelsAsync(CancellationToken.None);

            var listed = Only.Of(provider.Collector.GetSnapshot());
            Assert.Equal(1004, listed.Id.Id);
            Assert.Equal(LogLevel.Debug, listed.Level);
            Assert.Equal("1", Field(listed, "ModelCount"));
            Assert.Equal("TypeSafe", Field(listed, "Provider"));
        }
    }

    [Fact]
    public async Task AModelListingOnOpenRouter_LogsAFailure_WithoutSendingARequest()
    {
        var options = Options();
        options.Provider = DecisionProvider.OpenRouter;
        var (http, requests) = ScriptedDecision.Http(options, Reply.Ok(ModelsResponse));
        using (http)
        using (var provider = new FakeLoggerProvider())
        using (var loggers = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Debug)))
        using (var client = new DecisionClient(http, options, loggers))
        {
            await client.ListModelsAsync(CancellationToken.None);

            var failed = Only.Of(provider.Collector.GetSnapshot());
            Assert.Equal(1005, failed.Id.Id);
            Assert.Equal(LogLevel.Warning, failed.Level);
            Assert.Equal("Unsupported", Field(failed, "ErrorKind"));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task AnUnexpectedException_IsLoggedAsAnError_AndSurfacesUnchanged_ButYourCancellationIsNotLogged()
    {
        var options = Options();
        using var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://docs.example/api/") };
        using var provider = new FakeLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Debug));
        using var client = new DecisionClient(http, options, loggers);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.EvaluateAsync<ObservedUrgency>("Help!", CancellationToken.None));

        var record = Only.Of(provider.Collector.GetSnapshot());
        Assert.Equal(1006, record.Id.Id);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Same(thrown, record.Exception);
        Assert.Equal("evaluate-set", Field(record, "Operation"));

        provider.Collector.Clear();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.EvaluateAsync<ObservedUrgency>("Help!", cancelled.Token));
        Assert.DoesNotContain(provider.Collector.GetSnapshot(), logged => logged.Id.Id == 1006);
    }

    [Fact]
    public async Task WithTheLevelsTurnedOff_NothingIsLogged()
    {
        var options = Options();
        var (http, _) = ScriptedDecision.Http(options, Reply.Ok(UrgentResponse));
        using (http)
        using (var provider = new FakeLoggerProvider())
        using (var loggers = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.None)))
        using (var client = new DecisionClient(http, options, loggers))
        {
            var result = await client.EvaluateAsync<ObservedUrgency>("Help!", CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(provider.Collector.GetSnapshot());
        }
    }

    // The table's ids, event names, levels and message templates are DecisionLog's, read from its source.
    [Fact]
    public void TheEventTableOnThePage_IsDecisionLogsLoggerMessageAttributes()
    {
        var rows = PageTables.Rows(Page, "The events");

        Assert.Equal(
            Events().Select(e => $"{e.Id} {e.Name} {e.Level} {e.Template}"),
            rows.Select(row => $"{row[0]} {PageTables.Code(row[1])} {row[2]} {PageTables.Code(row[3])}"));
    }

    [Fact]
    public void TheEventTable_ReadsSixEvents()
    {
        Assert.Equal([1001, 1002, 1003, 1004, 1005, 1006], Events().Select(e => e.Id));
    }

    // The two fixed messages on the page are the constants DecisionLog logs instead of the error's own message.
    [Fact]
    public void TheFixedMessagesOnThePage_AreDecisionLogsConstants()
    {
        var source = File.ReadAllText(Path.Combine(PublishedPages.Root, "src", "Minos.NET", "DecisionLog.cs"));
        var rows = PageTables.Rows(Page, "What is never logged");

        Assert.Equal(["InvalidResponse", "Network"], rows.Select(row => PageTables.Code(row[0])));
        Assert.Contains($"UnreadableResponse = \"{PageTables.Code(rows[0][1])}\"", source, StringComparison.Ordinal);
        Assert.Contains($"RequestNotSent = \"{PageTables.Code(rows[1][1])}\"", source, StringComparison.Ordinal);
    }

    private static (int Id, string Name, string Level, string Template)[] Events()
    {
        var source = File.ReadAllText(Path.Combine(PublishedPages.Root, "src", "Minos.NET", "DecisionLog.cs"));
        var events = new List<(int, string, string, string)>();
        foreach (Match match in LoggerMessage().Matches(source))
        {
            events.Add((
                int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture),
                match.Groups["name"].Value,
                match.Groups["level"].Value,
                Template(match.Groups["message"].Value)));
        }

        return [.. events];
    }

    // The attribute's Message is a string literal, or literals joined by + with the ListModels constant.
    private static string Template(string expression)
    {
        var template = new StringBuilder();
        foreach (var part in expression.Split('+'))
        {
            var text = part.Trim();
            template.Append(text.StartsWith('"') ? text.Trim('"') : string.Equals(text, "ListModels", StringComparison.Ordinal) ? "list-models" : text);
        }

        return template.ToString();
    }

    [GeneratedRegex(
        @"\[LoggerMessage\(\s*EventId = (?<id>\d+),\s*EventName = nameof\((?<name>\w+)\),\s*Level = LogLevel\.(?<level>\w+),\s*Message = (?<message>.*?)\)\]",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex LoggerMessage();

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("A bug in a handler of the caller's own.");
    }
}
