using System.Net;
using Microsoft.Extensions.Logging;

namespace Minos.AotSmoke;

/// <summary>The client's log events under Native AOT, through a real <see cref="LoggerFactory"/> over an in-process provider.</summary>
internal static class LoggingChecks
{
    [Covers("Minos.DecisionClient.DecisionClient(System.Net.Http.HttpClient! httpClient, Minos.DecisionClientOptions? options, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory) -> void")]
    public static async Task RetriedEvaluationLogsTheRetryAndTheSuccess()
    {
        using var provider = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([provider], new LoggerFilterOptions { MinLevel = LogLevel.Debug });
        var handler = new SequenceHandler(Program.NoulResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(
            http, new DecisionClientOptions { ApiKey = "smoke-key", InitialBackoff = TimeSpan.FromMilliseconds(10) }, factory);

        var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);
        var records = provider.Records;

        Program.Check(
            result.IsSuccess
                && records.Length == 2
                && records[0] is { EventId: 1003, Level: LogLevel.Warning, Category: "Minos.DecisionClient" }
                && records[0].Field("Attempt") is 1
                && records[0].Field("StatusCode") is 503
                && records[1] is { EventId: 1001, Level: LogLevel.Debug }
                && records[1].Field("Operation") is "evaluate"
                && records[1].Field("QuestionCount") is 1,
            "a retried evaluation logs AttemptRetrying, then EvaluationSucceeded, under Native AOT");
    }

    public static async Task TypedEvaluationLogsItsQuestionCount()
    {
        using var provider = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([provider], new LoggerFilterOptions { MinLevel = LogLevel.Debug });
        using var http = Http(HttpStatusCode.OK, Program.TriageResponse);
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" }, factory);

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);
        var records = provider.Records;

        Program.Check(
            result.IsSuccess
                && records.Length == 1
                && records[0].EventId == 1001
                && records[0].Field("Operation") is "evaluate-set"
                && records[0].Field("QuestionCount") is 3,
            "a typed evaluation logs its generated set's question count under Native AOT");
    }

    public static async Task FailedEvaluationLogsTheLibraryMessageOnly()
    {
        using var provider = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([provider], new LoggerFilterOptions { MinLevel = LogLevel.Debug });
        using var http = Http(HttpStatusCode.UnprocessableEntity, Program.ValidationResponse);
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" }, factory);

        var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);
        var records = provider.Records;

        Program.Check(
            result.IsFailure
                && records.Length == 1
                && records[0] is { EventId: 1002, Level: LogLevel.Warning }
                && records[0].Field("ErrorKind") is DecisionErrorKind.Validation
                && records[0].Field("StatusCode") is 422
                && records[0].Field("ErrorMessage") is "The API returned HTTP 422."
                && !records[0].Message.Contains("is required", StringComparison.Ordinal),
            "a failed evaluation logs EvaluationFailed without the error body under Native AOT");
    }

    public static async Task ModelListingLogsTheModelCount()
    {
        using var provider = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([provider], new LoggerFilterOptions { MinLevel = LogLevel.Debug });
        using var http = Http(HttpStatusCode.OK, Program.ModelsResponse);
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" }, factory);

        var result = await client.ListModelsAsync().ConfigureAwait(false);
        var records = provider.Records;

        Program.Check(
            result.IsSuccess && records.Length == 1 && records[0].EventId == 1004 && records[0].Field("ModelCount") is 1,
            "a model listing logs ModelsListed under Native AOT");
    }

    private static HttpClient Http(HttpStatusCode status, string body)
        => new(new CannedHandler(status, body)) { BaseAddress = new Uri("https://example.test/api/") };
}
