using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Minos.Transport;
using ZeroAlloc.Resilience;
using ZeroAlloc.Results;

namespace Minos.Tests;

/// <summary>The decorator between the retry proxy and the transport, which logs each attempt the proxy will retry.</summary>
public sealed class LoggingDecisionApiTests
{
    private static readonly RetryPolicy ThreeAttempts = new(maxAttempts: 3, backoffMs: 1, jitter: false, perAttemptTimeoutMs: 0, maxDelayMs: 10);
    private static readonly DecisionError Overloaded = new(DecisionErrorKind.Overloaded, "The API returned HTTP 503.") { StatusCode = 503, RetryAfter = TimeSpan.FromSeconds(2) };

    [Theory]
    [InlineData(null, 1)]
    [InlineData(1, 2)]
    public async Task TransientFailure_BeforeTheLastAttempt_LogsAttemptRetrying(int? retryCount, int attempt)
    {
        var logger = new FakeLogger();
        var api = new LoggingDecisionApi(new FixedApi(Overloaded), logger, ThreeAttempts);

        var result = await api.ListModelsAsync("Bearer k", retryCount, CancellationToken.None);

        Assert.True(result.IsFailure);
        var record = Only(logger);
        Assert.Equal(1003, record.Id.Id);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(attempt.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "Attempt"));
        Assert.Equal("Overloaded", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("503", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("00:00:02", LogAssert.Field(record, "RetryAfter"));
    }

    [Fact]
    public async Task TransientFailure_OnTheLastAttempt_LogsNothing()
    {
        var logger = new FakeLogger();
        var api = new LoggingDecisionApi(new FixedApi(Overloaded), logger, ThreeAttempts);

        await api.ListModelsAsync("Bearer k", retryCount: 2, CancellationToken.None);

        Assert.Equal(0, logger.Collector.Count);
    }

    [Fact]
    public async Task Status408_IsTransient()
    {
        var logger = new FakeLogger();
        var api = new LoggingDecisionApi(new FixedApi(new DecisionError(DecisionErrorKind.Http, "The API returned HTTP 408.") { StatusCode = 408 }), logger, ThreeAttempts);

        await api.ListModelsAsync("Bearer k", retryCount: null, CancellationToken.None);

        Assert.Equal("408", LogAssert.Field(Only(logger), "StatusCode"));
    }

    [Fact]
    public async Task NonTransientFailure_LogsNothing()
    {
        var logger = new FakeLogger();
        var api = new LoggingDecisionApi(new FixedApi(new DecisionError(DecisionErrorKind.Unauthorized, "The API returned HTTP 401.") { StatusCode = 401 }), logger, ThreeAttempts);

        await api.ListModelsAsync("Bearer k", retryCount: null, CancellationToken.None);

        Assert.Equal(0, logger.Collector.Count);
    }

    [Fact]
    public async Task Success_LogsNothing()
    {
        var logger = new FakeLogger();
        var api = new LoggingDecisionApi(new FixedApi(error: null), logger, ThreeAttempts);

        var result = await api.ListModelsAsync("Bearer k", retryCount: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, logger.Collector.Count);
    }

    [Fact]
    public async Task EveryMethod_IsObserved()
    {
        var logger = new FakeLogger();
        var api = new LoggingDecisionApi(new FixedApi(Overloaded), logger, ThreeAttempts);

        await api.EvaluateAsync(null!, "Bearer k", retryCount: null, CancellationToken.None);
        await api.EvaluateRawAsync(null!, "Bearer k", retryCount: null, CancellationToken.None);
        await api.SendAsync(null!, "v1/systemone", "Bearer k", retryCount: null, CancellationToken.None);
        await api.ListModelsAsync("Bearer k", retryCount: null, CancellationToken.None);

        Assert.Equal(4, logger.Collector.Count);
    }

    [Fact]
    public void WarningDisabled_ReturnsTheInnerValueTaskItself()
    {
        var logger = new FakeLogger();
        logger.ControlLevel(LogLevel.Warning, enabled: false);
        var pending = new TaskCompletionSource<Result<ModelList, DecisionError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new LoggingDecisionApi(new PendingApi(pending.Task), logger, ThreeAttempts);

        Assert.Same(pending.Task, api.ListModelsAsync("Bearer k", retryCount: null, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task WarningDisabled_SynchronousTransientFailure_LogsNothingAndReturnsTheResultUnchanged()
    {
        var logger = new FakeLogger();
        logger.ControlLevel(LogLevel.Warning, enabled: false);
        var api = new LoggingDecisionApi(new FixedApi(Overloaded), logger, ThreeAttempts);

        var result = await api.ListModelsAsync("Bearer k", retryCount: null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Overloaded, result.Error.Kind);
        Assert.Equal(503, result.Error.StatusCode);
        Assert.Equal(0, logger.Collector.Count);
    }

    [Fact]
    public async Task AsynchronousFailure_IsLoggedWhenItCompletes()
    {
        var logger = new FakeLogger();
        var pending = new TaskCompletionSource<Result<ModelList, DecisionError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new LoggingDecisionApi(new PendingApi(pending.Task), logger, ThreeAttempts);

        var call = api.ListModelsAsync("Bearer k", retryCount: null, CancellationToken.None).AsTask();
        Assert.Equal(0, logger.Collector.Count);
        pending.SetResult(Result<ModelList, DecisionError>.Failure(Overloaded));
        await call;

        Assert.Equal(1003, Only(logger).Id.Id);
    }

    private static FakeLogRecord Only(FakeLogger logger)
    {
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        return Assert.Single(logger.Collector.GetSnapshot());
#pragma warning restore HLQ005
    }

    // Every method fails with the given error; with no error, ListModelsAsync succeeds and the others fail as overloaded.
    private sealed class FixedApi(DecisionError? error) : IDecisionApi
    {
        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
            => new(Result<SystemOneResponse, DecisionError>.Failure(error ?? Overloaded));

        public ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
            => new(Result<RawJson, DecisionError>.Failure(error ?? Overloaded));

        public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
            => new(Result<RawJson, DecisionError>.Failure(error ?? Overloaded));

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
            => new(error is null
                ? Result<ModelList, DecisionError>.Success(new ModelList { Models = [] })
                : Result<ModelList, DecisionError>.Failure(error));
    }

    // ListModelsAsync completes when the test completes the task.
    private sealed class PendingApi(Task<Result<ModelList, DecisionError>> pending) : IDecisionApi
    {
        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
            => new(pending);
    }
}
