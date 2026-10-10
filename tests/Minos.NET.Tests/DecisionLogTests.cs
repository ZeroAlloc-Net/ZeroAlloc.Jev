using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Minos.Tests;

/// <summary>The client's log events: their ids, names, levels, fields and guards.</summary>
public sealed class DecisionLogTests
{
    [Fact]
    public void EveryEvent_HasItsIdNameAndLevel()
    {
        var logger = new FakeLogger();

        DecisionLog.EvaluationSucceeded(logger, DecisionLog.Evaluate, "m", "TypeSafe", 1, 2.5);
        DecisionLog.EvaluationFailed(logger, DecisionLog.Evaluate, "m", DecisionErrorKind.Server, 500, 2.5, "The API returned HTTP 500.");
        DecisionLog.AttemptRetrying(logger, 1, DecisionErrorKind.Server, 500, null);
        DecisionLog.ModelsListed(logger, DecisionProvider.TypeSafe, 2, 2.5);
        DecisionLog.ModelsListFailed(logger, DecisionProvider.TypeSafe, DecisionErrorKind.Unauthorized, 401, 2.5, "The API returned HTTP 401.");
        DecisionLog.UnexpectedException(logger, DecisionLog.ListModels, new InvalidOperationException("bug"));

        (int, string, LogLevel)[] expected =
        [
            (1001, "EvaluationSucceeded", LogLevel.Debug),
            (1002, "EvaluationFailed", LogLevel.Warning),
            (1003, "AttemptRetrying", LogLevel.Warning),
            (1004, "ModelsListed", LogLevel.Debug),
            (1005, "ModelsListFailed", LogLevel.Warning),
            (1006, "UnexpectedException", LogLevel.Error),
        ];
        var actual = logger.Collector.GetSnapshot().Select(record => (record.Id.Id, record.Id.Name ?? string.Empty, record.Level)).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GeneratedEvents_CarryTheirFields()
    {
        var logger = new FakeLogger();

        DecisionLog.EvaluationSucceeded(logger, DecisionLog.EvaluateSet, "jev-latest", "openrouter", 4, 2.5);
        var succeeded = logger.LatestRecord;
        Assert.Equal("evaluate-set", LogAssert.Field(succeeded, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(succeeded, "Model"));
        Assert.Equal("openrouter", LogAssert.Field(succeeded, "Provider"));
        Assert.Equal("4", LogAssert.Field(succeeded, "QuestionCount"));
        Assert.NotNull(LogAssert.Field(succeeded, "DurationMs"));

        DecisionLog.AttemptRetrying(logger, 2, DecisionErrorKind.RateLimited, 429, TimeSpan.FromSeconds(3));
        var retrying = logger.LatestRecord;
        Assert.Equal("2", LogAssert.Field(retrying, "Attempt"));
        Assert.Equal("RateLimited", LogAssert.Field(retrying, "ErrorKind"));
        Assert.Equal("429", LogAssert.Field(retrying, "StatusCode"));
        Assert.Equal("00:00:03", LogAssert.Field(retrying, "RetryAfter"));

        DecisionLog.ModelsListed(logger, DecisionProvider.TypeSafe, 7, 2.5);
        Assert.Equal("7", LogAssert.Field(logger.LatestRecord, "ModelCount"));

        DecisionLog.ModelsListFailed(logger, DecisionProvider.TypeSafe, DecisionErrorKind.Network, null, 2.5, "Connection refused");
        var failed = logger.LatestRecord;
        Assert.Equal("Network", LogAssert.Field(failed, "ErrorKind"));
        Assert.Null(LogAssert.Field(failed, "StatusCode"));
        Assert.Equal("Connection refused", LogAssert.Field(failed, "ErrorMessage"));
    }

    [Fact]
    public void EvaluationSucceeded_CarriesItsValuesAndTemplate()
    {
        var logger = new FakeLogger();

        DecisionLog.EvaluationSucceeded(logger, DecisionLog.Evaluate, "jev-latest", "TypeSafe", 3, 2.5);

        var record = logger.LatestRecord;
        Assert.Equal("evaluate", LogAssert.Field(record, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(record, "Model"));
        Assert.Equal("TypeSafe", LogAssert.Field(record, "Provider"));
        Assert.Equal("3", LogAssert.Field(record, "QuestionCount"));
        Assert.Equal(2.5.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "DurationMs"));
        Assert.Equal(
            "Minos {Operation} on {Model} via {Provider} succeeded: {QuestionCount} questions in {DurationMs} ms.",
            LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Minos evaluate on jev-latest via TypeSafe succeeded: 3 questions in 2.5 ms.", record.Message);
    }

    [Fact]
    public void AttemptRetrying_CarriesItsValuesAndTemplate()
    {
        var logger = new FakeLogger();

        DecisionLog.AttemptRetrying(logger, 2, DecisionErrorKind.RateLimited, 429, TimeSpan.FromSeconds(3));

        var record = logger.LatestRecord;
        Assert.Equal("2", LogAssert.Field(record, "Attempt"));
        Assert.Equal("RateLimited", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("429", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("00:00:03", LogAssert.Field(record, "RetryAfter"));
        Assert.Equal(
            "Minos attempt {Attempt} failed with {ErrorKind}, status {StatusCode}, retry-after {RetryAfter}; retrying.",
            LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Minos attempt 2 failed with RateLimited, status 429, retry-after 00:00:03; retrying.", record.Message);
    }

    [Fact]
    public void AttemptRetrying_LogsANullRetryAfter_WhenTheServerSentNone()
    {
        var logger = new FakeLogger();

        DecisionLog.AttemptRetrying(logger, 1, DecisionErrorKind.Server, 500, null);

        var record = logger.LatestRecord;
        Assert.Null(LogAssert.Field(record, "RetryAfter"));
        Assert.Equal("500", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("Minos attempt 1 failed with Server, status 500, retry-after (null); retrying.", record.Message);
    }

    [Fact]
    public void ModelsListed_CarriesItsValuesAndTemplate()
    {
        var logger = new FakeLogger();

        DecisionLog.ModelsListed(logger, DecisionProvider.OpenRouter, 7, 2.5);

        var record = logger.LatestRecord;
        Assert.Equal("OpenRouter", LogAssert.Field(record, "Provider"));
        Assert.Equal("7", LogAssert.Field(record, "ModelCount"));
        Assert.Equal(2.5.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "DurationMs"));
        Assert.Equal(
            "Minos list-models via {Provider} succeeded: {ModelCount} models in {DurationMs} ms.",
            LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Minos list-models via OpenRouter succeeded: 7 models in 2.5 ms.", record.Message);
    }

    [Fact]
    public void ModelsListFailed_CarriesItsValuesAndTemplate()
    {
        var logger = new FakeLogger();

        DecisionLog.ModelsListFailed(logger, DecisionProvider.OpenRouter, DecisionErrorKind.Unauthorized, 401, 2.5, "The API returned HTTP 401.");

        var record = logger.LatestRecord;
        Assert.Equal("OpenRouter", LogAssert.Field(record, "Provider"));
        Assert.Equal("Unauthorized", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("401", LogAssert.Field(record, "StatusCode"));
        Assert.Equal(2.5.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "DurationMs"));
        Assert.Equal("The API returned HTTP 401.", LogAssert.Field(record, "ErrorMessage"));
        Assert.Equal(
            "Minos list-models via {Provider} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}",
            LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Minos list-models via OpenRouter failed with Unauthorized, status 401, in 2.5 ms: The API returned HTTP 401.", record.Message);
    }

    [Fact]
    public void UnexpectedException_CarriesItsOperationAndTemplate()
    {
        var logger = new FakeLogger();

        DecisionLog.UnexpectedException(logger, DecisionLog.ListModels, new InvalidOperationException("bug"));

        var record = logger.LatestRecord;
        Assert.Equal("list-models", LogAssert.Field(record, "Operation"));
        Assert.Equal("Minos {Operation} threw an unexpected exception.", LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Minos list-models threw an unexpected exception.", record.Message);
    }

    [Fact]
    public void EvaluationFailed_CarriesItsSixFields()
    {
        var logger = new FakeLogger();

        DecisionLog.EvaluationFailed(logger, DecisionLog.EvaluateSet, "jev-latest", DecisionErrorKind.RateLimited, 429, 12.5, "The API returned HTTP 429.");

        var record = logger.LatestRecord;
        Assert.Equal("evaluate-set", LogAssert.Field(record, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(record, "Model"));
        Assert.Equal("RateLimited", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("429", LogAssert.Field(record, "StatusCode"));
        Assert.Equal(12.5.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "DurationMs"));
        Assert.Equal(
            "Minos evaluate-set on jev-latest failed with RateLimited, status 429, in 12.5 ms: The API returned HTTP 429.",
            record.Message);
        Assert.Equal("The API returned HTTP 429.", LogAssert.Field(record, "ErrorMessage"));
        Assert.Equal(
            "Minos {Operation} on {Model} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}",
            LogAssert.Field(record, "{OriginalFormat}"));

        // Provider is fixed per client and retry-after is on AttemptRetrying: neither is a field here.
        var keys = record.StructuredState!.Select(pair => pair.Key).ToArray();
        Assert.DoesNotContain("Provider", keys);
        Assert.DoesNotContain("RetryAfter", keys);
    }

    [Fact]
    public void EvaluationFailed_LogsANullStatus_WhenNoResponseArrived()
    {
        var logger = new FakeLogger();

        DecisionLog.EvaluationFailed(logger, DecisionLog.Evaluate, "m", DecisionErrorKind.Timeout, null, 1, "The request timed out.");

        Assert.Null(LogAssert.Field(logger.LatestRecord, "StatusCode"));
    }

    [Fact]
    public void EvaluationFailed_LogsNothing_WhenWarningIsDisabled()
    {
        var logger = new FakeLogger();
        logger.ControlLevel(LogLevel.Warning, enabled: false);

        DecisionLog.EvaluationFailed(logger, DecisionLog.Evaluate, "m", DecisionErrorKind.Server, 500, 1, "x");

        Assert.Equal(0, logger.Collector.Count);
    }

    [Theory]
    [InlineData(DecisionErrorKind.Validation, "The API returned HTTP 422.")]
    [InlineData(DecisionErrorKind.Timeout, "The request timed out.")]
    [InlineData(DecisionErrorKind.Unsupported, "Model listing is only available on TypeSafe's API.")]
    public void SafeMessage_KeepsALibraryMessage(DecisionErrorKind kind, string message)
        => Assert.Equal(message, DecisionLog.SafeMessage(new DecisionError(kind, message)));

    [Fact]
    public void SafeMessage_ReplacesANetworkMessage_WhichCanEchoTheRequest()
        => Assert.Equal(
            DecisionLog.RequestNotSent,
            DecisionLog.SafeMessage(new DecisionError(DecisionErrorKind.Network, "Connection refused to https://host/?q=secret")));

    [Fact]
    public void SafeMessage_ReplacesAnInvalidResponseMessage_WhichCanQuoteTheResponse()
        => Assert.Equal(
            DecisionLog.UnreadableResponse,
            DecisionLog.SafeMessage(new DecisionError(DecisionErrorKind.InvalidResponse, "'secret' is not one of the options.") { StatusCode = 200 }));

    [Theory]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public void IsAnyEnabled_IsTrue_WhenOnlyOneLevelAnOperationEmitsIsEnabled(LogLevel enabled)
    {
        var logger = new FakeLogger();
        foreach (var level in new[] { LogLevel.Debug, LogLevel.Warning, LogLevel.Error })
        {
            logger.ControlLevel(level, enabled: level == enabled);
        }

        Assert.True(DecisionLog.IsAnyEnabled(logger));
    }

    [Fact]
    public void IsAnyEnabled_IsFalse_WhenNoLevelAnOperationEmitsIsEnabled()
    {
        var none = new FakeLogger();
        none.ControlLevel(LogLevel.Debug, enabled: false);
        none.ControlLevel(LogLevel.Warning, enabled: false);
        none.ControlLevel(LogLevel.Error, enabled: false);

        Assert.True(DecisionLog.IsAnyEnabled(new FakeLogger()));
        Assert.False(DecisionLog.IsAnyEnabled(none));
        Assert.False(DecisionLog.IsAnyEnabled(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
    }

    [Fact]
    public void StartTiming_IsZero_WithoutALogger_OrWithDebugAndWarningDisabled()
    {
        var quiet = new FakeLogger();
        quiet.ControlLevel(LogLevel.Debug, enabled: false);
        quiet.ControlLevel(LogLevel.Warning, enabled: false);

        Assert.Equal(0L, DecisionLog.StartTiming(null));
        Assert.Equal(0L, DecisionLog.StartTiming(quiet));
        Assert.NotEqual(0L, DecisionLog.StartTiming(new FakeLogger()));
        Assert.Equal(0d, DecisionLog.ElapsedMilliseconds(0));
        Assert.True(DecisionLog.ElapsedMilliseconds(DecisionLog.StartTiming(new FakeLogger())) >= 0);
    }

    [Fact]
    public void LogUnexpected_LogsAnError_AndReturnsFalse_SoTheExceptionPropagates()
    {
        var logger = new FakeLogger();
        var bug = new InvalidOperationException("bug");

        Assert.False(DecisionLog.LogUnexpected(logger, DecisionLog.Evaluate, bug, CancellationToken.None));

        var record = logger.LatestRecord;
        Assert.Equal(1006, record.Id.Id);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal("evaluate", LogAssert.Field(record, "Operation"));
        Assert.Same(bug, record.Exception);
    }

    [Fact]
    public void LogUnexpected_SkipsTheCallersOwnCancellation()
    {
        var logger = new FakeLogger();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.False(DecisionLog.LogUnexpected(logger, DecisionLog.Evaluate, new OperationCanceledException(cancellation.Token), cancellation.Token));

        Assert.Equal(0, logger.Collector.Count);
    }
}
