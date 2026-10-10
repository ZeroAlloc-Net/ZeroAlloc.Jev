using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Minos.Serialization;

namespace Minos.Tests;

/// <summary>What <see cref="DecisionClient"/> logs through an <see cref="ILoggerFactory"/>.</summary>
public sealed class DecisionClientLoggingTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public async Task RetriedAttempt_LogsAttemptRetrying_WithItsNumber()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Sequence(() => Status(503), Success), logs.Factory, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        var retrying = logs.Only(1003);
        Assert.Equal(LogLevel.Warning, retrying.Level);
        Assert.Equal("1", LogAssert.Field(retrying, "Attempt"));
        Assert.Equal("Overloaded", LogAssert.Field(retrying, "ErrorKind"));
        Assert.Equal("503", LogAssert.Field(retrying, "StatusCode"));
        Assert.Null(LogAssert.Field(retrying, "RetryAfter"));
    }

    [Fact]
    public async Task ExhaustedRetries_LogOnePerRetry_AndNoneForTheLastAttempt()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(() => Status(503));
        using var client = Client(handler, logs.Factory, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsFailure);
        Assert.Equal(3, handler.Requests.Count);
        var attempts = logs.Records.Where(record => record.Id.Id == 1003).Select(record => LogAssert.Field(record, "Attempt")!).ToArray();
        Assert.Equal(["1", "2"], attempts);
    }

    [Fact]
    public async Task RetryAfter_IsLogged()
    {
        using var logs = new LogCapture();
        var rateLimited = () =>
        {
            var response = Status(429);
            response.Headers.TryAddWithoutValidation("retry-after-ms", "20");
            return response;
        };
        using var client = Client(StubHandler.Sequence(rateLimited, Success), logs.Factory, maxRetries: 1);

        await client.EvaluateAsync(Request());

        var retrying = logs.Only(1003);
        Assert.Equal("RateLimited", LogAssert.Field(retrying, "ErrorKind"));
        Assert.Equal("00:00:00.0200000", LogAssert.Field(retrying, "RetryAfter"));
    }

    [Fact]
    public async Task EveryPath_LogsItsRetries()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(
            () => Status(503),
            Success,
            () => Status(503),
            Success,
            () => Status(503),
            () => Json(HttpStatusCode.OK, Fixture.Text("models.json")));
        using var client = Client(handler, logs.Factory, maxRetries: 1);

        Assert.True((await client.EvaluateAsync<UrgencyCheck>("Help!")).IsSuccess);
        Assert.True((await client.EvaluateAsync(OneNoulSet(), "Help!")).IsSuccess);
        Assert.True((await client.ListModelsAsync()).IsSuccess);

        Assert.Equal(3, logs.Records.Count(record => record.Id.Id == 1003));
    }

    [Fact]
    public async Task EveryLevelDisabled_LogsNothing()
    {
        using var logs = new LogCapture(LogLevel.None);
        using var client = Client(StubHandler.Sequence(() => Status(503), () => Status(422)), logs.Factory, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task NonTransientFailure_LogsNoRetry_AndSendsOneRequest()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(() => Status(422));
        using var client = Client(handler, logs.Factory, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single, not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        Assert.Single(handler.Requests);
#pragma warning restore HLQ005
        Assert.DoesNotContain(1003, logs.EventIds);
    }

    [Fact]
    public async Task TransientFailure_WithNoRetriesAllowed_LogsNoRetry()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(() => Status(503));
        using var client = Client(handler, logs.Factory, maxRetries: 0);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsFailure);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single, not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        Assert.Single(handler.Requests);
#pragma warning restore HLQ005
        Assert.DoesNotContain(1003, logs.EventIds);
    }

    [Fact]
    public async Task NullFactory_RetriesAsBefore()
    {
        var handler = StubHandler.Sequence(() => Status(503), Success);
        using var client = Client(handler, loggerFactory: null, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Logger_UsesTheDecisionClientCategory()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Sequence(() => Status(503), Success), logs.Factory, maxRetries: 1);

        await client.EvaluateAsync(Request());

        Assert.Equal("Minos.DecisionClient", logs.Only(1003).Category);
        Assert.Equal(DecisionLog.Category, logs.Only(1003).Category);
    }

    [Fact]
    public async Task Evaluate_Success_LogsEvaluationSucceeded()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")), logs.Factory);

        Assert.True((await client.EvaluateAsync(Request())).IsSuccess);

        var record = logs.Only(1001);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.Equal(DecisionLog.Evaluate, LogAssert.Field(record, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(record, "Model"));
        Assert.Equal("TypeSafe", LogAssert.Field(record, "Provider"));
        Assert.Equal("1", LogAssert.Field(record, "QuestionCount"));
        AssertDuration(record);
        Assert.Equal([1001], logs.EventIds);
    }

    [Fact]
    public async Task Evaluate_Failure_LogsEvaluationFailed_WithTheLibraryMessage()
    {
        using var logs = new LogCapture();
        using var client = Client(
            StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"detail":"questions.is_urgent is invalid"}"""), logs.Factory);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        var record = logs.Only(1002);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(DecisionLog.Evaluate, LogAssert.Field(record, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(record, "Model"));
        Assert.Equal("Validation", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("422", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("The API returned HTTP 422.", LogAssert.Field(record, "ErrorMessage"));
        AssertDuration(record);
    }

    [Fact]
    public async Task EveryTypedOverload_LogsEvaluateSet()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")), logs.Factory);
        using var document = JsonDocument.Parse("""{"message":"Help!"}""");

        Assert.True((await client.EvaluateAsync<UrgencyCheck>("Help!")).IsSuccess);
        Assert.True((await client.EvaluateAsync<UrgencyCheck>(document.RootElement)).IsSuccess);
        Assert.True((await client.EvaluateUtf8Async<UrgencyCheck>("\"Help!\""u8.ToArray())).IsSuccess);
        Assert.True((await client.EvaluateAsync<PaddedUrgency, PaddedState>(new PaddedState("\"Help!\""), PaddedStateJsonContext.Default.PaddedState)).IsSuccess);

        Assert.Equal([1001, 1001, 1001, 1001], logs.EventIds);
        Assert.All(logs.Records, record =>
        {
            Assert.Equal(DecisionLog.EvaluateSet, LogAssert.Field(record, "Operation"));
            Assert.Equal(ClientTestKit.TestModel, LogAssert.Field(record, "Model"));
            Assert.Equal("typesafe", LogAssert.Field(record, "Provider"));
            Assert.Equal("1", LogAssert.Field(record, "QuestionCount"));
        });
    }

    [Fact]
    public async Task Typed_InvalidResponse_LogsEvaluationFailed_WithoutQuotingTheResponse()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-choice.json")), logs.Factory);

        var result = await client.EvaluateAsync<UrgencyCheck>("Help!");

        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        var record = logs.Only(1002);
        Assert.Equal(DecisionLog.EvaluateSet, LogAssert.Field(record, "Operation"));
        Assert.Equal("InvalidResponse", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("200", LogAssert.Field(record, "StatusCode"));
        Assert.Equal(DecisionLog.UnreadableResponse, LogAssert.Field(record, "ErrorMessage"));
    }

    [Fact]
    public async Task BuiltSet_LogsEvaluateSet_WithItsQuestionCount()
    {
        const string response = """{"model":"m","answers":{"is_urgent":{"type":"noul","noul":0.9},"is_angry":{"type":"noul","noul":0.2}},"usage":{"input_tokens":1,"output_tokens":1}}""";
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, response), logs.Factory);
        var set = QuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _)
            .Noul("is_angry", "Is the customer angry?", out _)
            .Build()
            .Value;

        Assert.True((await client.EvaluateAsync(set, "Help!")).IsSuccess);

        var record = logs.Only(1001);
        Assert.Equal(DecisionLog.EvaluateSet, LogAssert.Field(record, "Operation"));
        Assert.Equal("2", LogAssert.Field(record, "QuestionCount"));
    }

    [Fact]
    public async Task BuiltSet_Failure_LogsEvaluationFailed()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"detail":"questions.is_urgent is invalid"}"""), logs.Factory);

        var result = await client.EvaluateAsync(OneNoulSet(), "Help!");

        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        var record = logs.Only(1002);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(DecisionLog.EvaluateSet, LogAssert.Field(record, "Operation"));
        Assert.Equal(ClientTestKit.TestModel, LogAssert.Field(record, "Model"));
        Assert.Equal("Validation", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("422", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("The API returned HTTP 422.", LogAssert.Field(record, "ErrorMessage"));
    }

    [Fact]
    public async Task ListModels_Success_LogsModelsListed()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json")), logs.Factory);

        Assert.True((await client.ListModelsAsync()).IsSuccess);

        var record = logs.Only(1004);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.Equal("TypeSafe", LogAssert.Field(record, "Provider"));
        Assert.Equal("2", LogAssert.Field(record, "ModelCount"));
        AssertDuration(record);
    }

    [Fact]
    public async Task ListModels_Failure_LogsModelsListFailed()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Json(HttpStatusCode.Unauthorized, """{"detail":"bad key"}"""), logs.Factory);

        Assert.True((await client.ListModelsAsync()).IsFailure);

        var record = logs.Only(1005);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("Unauthorized", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("401", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("The API returned HTTP 401.", LogAssert.Field(record, "ErrorMessage"));
    }

    [Fact]
    public async Task ListModels_OnOpenRouter_LogsUnsupported_WithoutARequest()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json"));
        using var client = Client(handler, logs.Factory, provider: DecisionProvider.OpenRouter);

        Assert.True((await client.ListModelsAsync()).IsFailure);

        var record = logs.Only(1005);
        Assert.Equal("OpenRouter", LogAssert.Field(record, "Provider"));
        Assert.Equal("Unsupported", LogAssert.Field(record, "ErrorKind"));
        Assert.Null(LogAssert.Field(record, "StatusCode"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnexpectedException_LogsError_AndSurfacesUnchanged()
    {
        using var logs = new LogCapture();
        using var client = Client(new StubHandler((_, _) => throw new InvalidOperationException("bug")), logs.Factory);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.EvaluateAsync(Request()));

        Assert.Equal("bug", thrown.Message);
        var record = logs.Only(1006);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal(DecisionLog.Evaluate, LogAssert.Field(record, "Operation"));
        Assert.Same(thrown, record.Exception);
        Assert.Equal([1006], logs.EventIds);
    }

    [Fact]
    public async Task UnexpectedException_InListModels_LogsItsOperation()
    {
        using var logs = new LogCapture();
        using var client = Client(new StubHandler((_, _) => throw new InvalidOperationException("bug")), logs.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ListModelsAsync());

        Assert.Equal(DecisionLog.ListModels, LogAssert.Field(logs.Only(1006), "Operation"));
    }

    [Fact]
    public async Task CallerCancellation_LogsNothing()
    {
        using var logs = new LogCapture();
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(async (_, ct) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return Success();
        });
        using var client = Client(handler, logs.Factory);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.EvaluateAsync(Request(), cancellation.Token));

        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task DebugDisabled_LogsFailuresButNotSuccesses()
    {
        using var logs = new LogCapture(LogLevel.Warning);
        using var client = Client(StubHandler.Sequence(Success, () => Status(422)), logs.Factory);

        Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
        Assert.True((await client.EvaluateAsync(Request())).IsFailure);

        Assert.Equal([1002], logs.EventIds);
    }

    [Fact]
    public async Task RequestWithoutQuestions_HasTheSameOutcome_WithAndWithoutALogger()
    {
        var unlogged = await OutcomeOfAQuestionlessRequestAsync(loggerFactory: null);
        using var logs = new LogCapture();
        var logged = await OutcomeOfAQuestionlessRequestAsync(logs.Factory);

        Assert.Equal(unlogged, logged);
    }

    // The exception type the call ends in, or the result's error kind, so a logger cannot change which.
    private async Task<string> OutcomeOfAQuestionlessRequestAsync(ILoggerFactory? loggerFactory)
    {
        using var client = Client(StubHandler.Sequence(Success), loggerFactory);
        var request = new SystemOneRequest { State = "state", Questions = null! };

        try
        {
            var result = await client.EvaluateAsync(request);
            return result.IsSuccess ? "success" : result.Error.Kind.ToString();
        }
        catch (Exception exception)
        {
            return exception.GetType().FullName!;
        }
    }

    private DecisionClient Client(StubHandler handler, ILoggerFactory? loggerFactory, int maxRetries = 0, DecisionProvider provider = DecisionProvider.TypeSafe)
    {
        var http = new HttpClient(handler);
        _httpClients.Add(http);
        return new DecisionClient(
            http,
            new DecisionClientOptions
            {
                ApiKey = "test-key",
                Provider = provider,
                Model = ClientTestKit.TestModel,
                MaxRetries = maxRetries,
                InitialBackoff = TimeSpan.FromMilliseconds(1),
                Jitter = false,
            },
            loggerFactory);
    }

    private static QuestionSet OneNoulSet()
        => QuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out _).Build().Value;

    private static HttpResponseMessage Success() => Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));

    private static HttpResponseMessage Status(int status) => Json((HttpStatusCode)status, "{}");

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SystemOneRequest Request()
        => JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;

    private static void AssertDuration(FakeLogRecord record)
        => Assert.True(double.Parse(LogAssert.Field(record, "DurationMs")!, CultureInfo.InvariantCulture) >= 0);
}
