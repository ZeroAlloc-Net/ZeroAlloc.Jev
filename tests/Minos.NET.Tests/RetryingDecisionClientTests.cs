using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;

namespace Minos.Tests;

public sealed class RetryingDecisionClientTests
{
    private static readonly DecisionRequest Request = new(QuestionSets.UrgencyDefinition(), "s");

    private sealed class Scripted(params Result<DecisionResponse, DecisionError>[] results) : IDecisionClient
    {
        private int _next;

        public List<DecisionRequest> Requests { get; } = [];

        public bool Disposed { get; private set; }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new(results[Math.Min(_next++, results.Length - 1)]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Disposed = true;
    }

    private static Result<DecisionResponse, DecisionError> Fail(DecisionErrorKind kind, int? status = null, TimeSpan? retryAfter = null)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, "x") { StatusCode = status, RetryAfter = retryAfter });

    private static Result<DecisionResponse, DecisionError> Ok()
        => Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(Request.Definition, [QuestionAnswer.Noul(0.5)]));

    [Theory]
    [InlineData(DecisionErrorKind.RateLimited, null)]
    [InlineData(DecisionErrorKind.Overloaded, null)]
    [InlineData(DecisionErrorKind.Server, null)]
    [InlineData(DecisionErrorKind.Network, null)]
    [InlineData(DecisionErrorKind.Timeout, null)]
    [InlineData(DecisionErrorKind.Http, 408)]
    public async Task Retries_transient_failures_and_stamps_the_attempt(DecisionErrorKind kind, int? status)
    {
        var inner = new Scripted(Fail(kind, status), Ok());
        var time = new FakeTimeProvider();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, timeProvider: time);

        var call = stage.EvaluateAsync(Request).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(500));
        var result = await call;

        Assert.True(result.IsSuccess);
        Assert.Equal([0, 1], inner.Requests.Select(r => r.RetryAttempt));
        Assert.Same(Request, inner.Requests[0]);
    }

    [Theory]
    [InlineData(DecisionErrorKind.Validation)]
    [InlineData(DecisionErrorKind.Unauthorized)]
    [InlineData(DecisionErrorKind.InvalidResponse)]
    [InlineData(DecisionErrorKind.Disposed)]
    public async Task Does_not_retry_permanent_failures(DecisionErrorKind kind)
    {
        var inner = new Scripted(Fail(kind));
        using var stage = new RetryingDecisionClient(inner);

        var call = stage.EvaluateAsync(Request);

        Assert.True(call.IsCompletedSuccessfully);
        Assert.Equal(kind, (await call).Error.Kind);
        Assert.Equal([0], inner.Requests.Select(r => r.RetryAttempt));
    }

    [Fact]
    public async Task Stops_after_max_retries()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Server));
        var time = new FakeTimeProvider();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { MaxRetries = 2, Jitter = false }, timeProvider: time);

        var call = stage.EvaluateAsync(Request).AsTask();
        await Eventually(() =>
        {
            time.Advance(TimeSpan.FromSeconds(30));
            return call.IsCompleted;
        });

        Assert.Equal(DecisionErrorKind.Server, (await call).Error.Kind);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task Retry_after_replaces_the_backoff()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.RateLimited, 429, TimeSpan.FromSeconds(7)), Ok());
        var time = new FakeTimeProvider();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, timeProvider: time);

        var call = stage.EvaluateAsync(Request).AsTask();
        time.Advance(TimeSpan.FromSeconds(6.9));
        await Task.Yield();
        Assert.Equal([0], inner.Requests.Select(r => r.RetryAttempt));
        time.Advance(TimeSpan.FromSeconds(0.2));

        Assert.True((await call).IsSuccess);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task Should_retry_can_be_replaced()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Validation), Ok());
        var time = new FakeTimeProvider();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { ShouldRetry = e => e.Kind == DecisionErrorKind.Validation, Jitter = false }, timeProvider: time);

        var call = stage.EvaluateAsync(Request).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.True((await call).IsSuccess);
    }

    [Fact]
    public async Task Disposed_is_never_retried_even_when_should_retry_says_so()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Disposed));
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { ShouldRetry = _ => true });

        await stage.EvaluateAsync(Request);

        Assert.Equal([0], inner.Requests.Select(r => r.RetryAttempt));
    }

    [Fact]
    public async Task No_retry_starts_after_dispose()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Server), Ok());
        var time = new FakeTimeProvider();
        var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, timeProvider: time);

        var call = stage.EvaluateAsync(Request).AsTask();
        stage.Dispose();
        time.Advance(TimeSpan.FromSeconds(1));
        var result = await call;

        Assert.Equal(DecisionErrorKind.Disposed, result.Error.Kind);
        Assert.Equal([0], inner.Requests.Select(r => r.RetryAttempt));
        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task Logs_each_retried_attempt()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Server, 500), Fail(DecisionErrorKind.Server, 500), Ok());
        var time = new FakeTimeProvider();
        using var logs = new LogCapture();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, Logger(logs), time);

        var call = stage.EvaluateAsync(Request).AsTask();
        await Eventually(() =>
        {
            time.Advance(TimeSpan.FromSeconds(30));
            return call.IsCompleted;
        });

        await call;
        Assert.Equal(["1", "2"], logs.Records.Where(r => r.Id.Id == 1003).Select(r => LogAssert.Field(r, "Attempt")));
        var first = logs.Records.First(r => r.Id.Id == 1003);
        Assert.Equal("Server", LogAssert.Field(first, "ErrorKind"));
        Assert.Equal("500", LogAssert.Field(first, "StatusCode"));
        Assert.Equal(LogLevel.Warning, first.Level);
    }

    [Fact]
    public async Task Cancellation_during_the_wait_throws()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Server), Ok());
        using var cts = new CancellationTokenSource();
        using var stage = new RetryingDecisionClient(inner, timeProvider: new FakeTimeProvider());

        var call = stage.EvaluateAsync(Request, cts.Token).AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task Use_retries_reads_defaults_from_the_inner_client()
    {
        var inner = new OptionsOffering(new DecisionRetryOptions { MaxRetries = 0 });
        using var client = inner.AsBuilder().UseRetries().Build();

        await client.EvaluateAsync(Request);

        Assert.Equal([0], inner.Requests.Select(r => r.RetryAttempt));
    }

    [Fact]
    public async Task Use_retries_configure_changes_a_copy_and_the_stage_uses_it()
    {
        var offered = new DecisionRetryOptions { MaxRetries = 1 };
        var inner = new OptionsOffering(offered);
        var time = new FakeTimeProvider();
        using var client = inner.AsBuilder().UseRetries(o =>
        {
            o.MaxRetries = 4;
            o.Jitter = false;
        }).Build(new Services(time, null));

        var call = client.EvaluateAsync(Request).AsTask();
        await Eventually(() =>
        {
            time.Advance(TimeSpan.FromSeconds(30));
            return call.IsCompleted;
        });
        await call;

        Assert.Equal(1, offered.MaxRetries);
        Assert.Equal([0, 1, 2, 3, 4], inner.Requests.Select(r => r.RetryAttempt));
        Assert.NotNull(client.GetService<RetryingDecisionClient>());
    }

    [Fact]
    public async Task Use_retries_takes_the_logger_factory_and_time_provider_from_the_build_services()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Server), Fail(DecisionErrorKind.Server), Ok());
        var time = new FakeTimeProvider();
        using var logs = new LogCapture();
        using var client = inner.AsBuilder().UseRetries(o => o.Jitter = false).Build(new Services(time, logs.Factory));

        var call = client.EvaluateAsync(Request).AsTask();
        await Eventually(() =>
        {
            time.Advance(TimeSpan.FromSeconds(30));
            return call.IsCompleted;
        });

        Assert.True((await call).IsSuccess);
        Assert.Equal(3, inner.Requests.Count);
        Assert.Equal(["1", "2"], logs.Records.Where(r => r.Id.Id == 1003).Select(r => LogAssert.Field(r, "Attempt")));
    }

    [Fact]
    public async Task A_retry_that_meets_a_disposed_inner_client_after_the_flag_check_is_disposed()
    {
        var inner = new DisposesOnRetry();
        var time = new FakeTimeProvider();
        var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, timeProvider: time);
        inner.Stage = stage;

        var call = stage.EvaluateAsync(Request).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(500));
        var result = await call;

        Assert.Equal(DecisionErrorKind.Disposed, result.Error.Kind);
        Assert.Same(inner.Thrown, result.Error.Exception);
    }

    [Fact]
    public async Task An_object_disposed_exception_from_a_live_stage_propagates()
    {
        var inner = new DisposesOnRetry { DisposeStage = false };
        var time = new FakeTimeProvider();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, timeProvider: time);
        inner.Stage = stage;

        var call = stage.EvaluateAsync(Request).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Same(inner.Thrown, await Assert.ThrowsAsync<ObjectDisposedException>(() => call));
    }

    [Fact]
    public async Task Should_retry_judges_each_failure_once()
    {
        var judged = 0;
        var time = new FakeTimeProvider();
        var options = new DecisionRetryOptions
        {
            Jitter = false,
            ShouldRetry = e =>
            {
                Interlocked.Increment(ref judged);
                return DecisionRetryOptions.IsTransient(e);
            },
        };
        using var stage = new RetryingDecisionClient(new Scripted(Fail(DecisionErrorKind.Server), Fail(DecisionErrorKind.Server), Ok()), options, timeProvider: time);

        var call = stage.EvaluateAsync(Request).AsTask();
        await Eventually(() =>
        {
            time.Advance(TimeSpan.FromSeconds(30));
            return call.IsCompleted;
        });
        await call;

        Assert.Equal(2, Volatile.Read(ref judged));
    }

    [Fact]
    public async Task Should_retry_judges_an_asynchronous_first_failure_once()
    {
        var judged = 0;
        var pending = new TaskCompletionSource<Result<DecisionResponse, DecisionError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new FakeTimeProvider();
        var options = new DecisionRetryOptions
        {
            MaxRetries = 1,
            Jitter = false,
            ShouldRetry = e =>
            {
                Interlocked.Increment(ref judged);
                return true;
            },
        };
        using var stage = new RetryingDecisionClient(new Pending(pending.Task), options, timeProvider: time);

        var call = stage.EvaluateAsync(Request).AsTask();
        pending.SetResult(Fail(DecisionErrorKind.Server));
        await Eventually(() =>
        {
            time.Advance(TimeSpan.FromSeconds(30));
            return call.IsCompleted;
        });
        await call;

        // The first failure is retried and judged once; the retry's failure is the last attempt and is never judged.
        Assert.Equal(1, Volatile.Read(ref judged));
    }

    [Fact]
    public async Task An_asynchronously_completed_first_failure_that_is_retried_is_logged()
    {
        var pending = new TaskCompletionSource<Result<DecisionResponse, DecisionError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new FakeTimeProvider();
        using var logs = new LogCapture();
        using var stage = new RetryingDecisionClient(new Pending(pending.Task), new DecisionRetryOptions { Jitter = false }, Logger(logs), time);

        var call = stage.EvaluateAsync(Request).AsTask();
        Assert.Empty(logs.Records);
        pending.SetResult(Fail(DecisionErrorKind.Overloaded, 503));
        await Eventually(() =>
        {
            time.Advance(TimeSpan.FromSeconds(30));
            return call.IsCompleted;
        });
        await call;

        Assert.Equal("1", LogAssert.Field(logs.Records.First(r => r.Id.Id == 1003), "Attempt"));
    }

    [Fact]
    public async Task The_last_attempt_logs_nothing()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Server));
        using var logs = new LogCapture();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { MaxRetries = 0 }, Logger(logs));

        await stage.EvaluateAsync(Request);

        Assert.Empty(logs.Records);
        Assert.Equal([0], inner.Requests.Select(r => r.RetryAttempt));
    }

    [Fact]
    public async Task Permanent_failures_and_successes_log_nothing()
    {
        using var logs = new LogCapture();
        using var denied = new RetryingDecisionClient(new Scripted(Fail(DecisionErrorKind.Unauthorized, 401)), logger: Logger(logs));
        using var fine = new RetryingDecisionClient(new Scripted(Ok()), logger: Logger(logs));

        await denied.EvaluateAsync(Request);
        await fine.EvaluateAsync(Request);

        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task Retry_after_is_logged()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Overloaded, 503, TimeSpan.FromSeconds(2)), Ok());
        var time = new FakeTimeProvider();
        using var logs = new LogCapture();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, Logger(logs), time);

        var call = stage.EvaluateAsync(Request).AsTask();
        time.Advance(TimeSpan.FromSeconds(2));
        await call;

        Assert.Equal("00:00:02", LogAssert.Field(logs.Only(1003), "RetryAfter"));
    }

    [Fact]
    public async Task A_disabled_warning_level_still_retries_and_logs_nothing()
    {
        var inner = new Scripted(Fail(DecisionErrorKind.Server), Ok());
        var time = new FakeTimeProvider();
        using var logs = new LogCapture(LogLevel.Error);
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, Logger(logs), time);

        var call = stage.EvaluateAsync(Request).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.True((await call).IsSuccess);
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task A_completed_success_is_returned_without_a_state_machine()
    {
        var inner = new Scripted(Ok());
        using var stage = new RetryingDecisionClient(inner);

        var call = stage.EvaluateAsync(Request);

        Assert.True(call.IsCompletedSuccessfully);
        Assert.True((await call).IsSuccess);
    }

    [Fact]
    public async Task A_pending_first_attempt_is_awaited_and_then_judged()
    {
        var pending = new TaskCompletionSource<Result<DecisionResponse, DecisionError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new Pending(pending.Task);
        var time = new FakeTimeProvider();
        using var stage = new RetryingDecisionClient(inner, new DecisionRetryOptions { Jitter = false }, timeProvider: time);

        var call = stage.EvaluateAsync(Request).AsTask();
        pending.SetResult(Fail(DecisionErrorKind.Server));
        await Eventually(() =>
        {
            time.Advance(TimeSpan.FromSeconds(30));
            return call.IsCompleted;
        });

        Assert.Equal(DecisionErrorKind.Server, (await call).Error.Kind);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task A_null_request_throws()
    {
        using var stage = new RetryingDecisionClient(new Scripted(Ok()));

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await stage.EvaluateAsync(null!));
    }

    [Fact]
    public void Invalid_options_are_rejected_by_the_constructor()
        => Assert.Throws<ArgumentException>(() => new RetryingDecisionClient(new Scripted(Ok()), new DecisionRetryOptions { MaxRetries = -1 }));

    [Fact]
    public void The_stage_is_found_through_get_service_and_the_inner_client_is_reached_too()
    {
        var inner = new OptionsOffering(new DecisionRetryOptions());
        using var stage = new RetryingDecisionClient(inner);

        Assert.Same(stage, stage.GetService<RetryingDecisionClient>());
        Assert.NotNull(stage.GetService<DecisionRetryOptions>());
    }

    private static ILogger Logger(LogCapture logs) => logs.Factory.CreateLogger(DecisionLog.Category);

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 5 seconds.");
            await Task.Delay(1);
        }
    }

    private sealed class Services(TimeProvider time, ILoggerFactory? loggers) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(TimeProvider) ? time : serviceType == typeof(ILoggerFactory) ? loggers : null;
    }

    // The first call fails; the second signals Dispose to the stage, as a concurrent Dispose would, and throws.
    private sealed class DisposesOnRetry : IDecisionClient
    {
        private int _calls;

        public RetryingDecisionClient? Stage { get; set; }

        public bool DisposeStage { get; init; } = true;

        public ObjectDisposedException Thrown { get; } = new("inner");

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (_calls++ == 0)
            {
                return new(Fail(DecisionErrorKind.Server));
            }

            if (DisposeStage)
            {
                Stage!.Dispose();
            }

            throw Thrown;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class Pending(Task<Result<DecisionResponse, DecisionError>> first) : IDecisionClient
    {
        public int Calls { get; private set; }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => Calls++ == 0 ? new(first) : new(Fail(DecisionErrorKind.Server));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class OptionsOffering(DecisionRetryOptions offered) : IDecisionClient
    {
        public DecisionRetryOptions Offered => offered;

        public List<DecisionRequest> Requests { get; } = [];

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new(Fail(DecisionErrorKind.Server));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(DecisionRetryOptions) ? offered : null;

        public void Dispose()
        {
        }
    }
}
