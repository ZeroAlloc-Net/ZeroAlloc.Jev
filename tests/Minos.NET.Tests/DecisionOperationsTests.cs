using System.Text.Json;
using Minos.Serialization;
using Minos.Telemetry;
using Minos.Transport;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace Minos.Tests;

/// <summary>
/// The raw System One calls under the telemetry proxy: their disposal guard, moved here from DisposalGuardDecisionApiTests
/// with DisposalGuardDecisionApi, and their retries through the shared loop.
/// </summary>
public sealed class DecisionOperationsTests
{
    private static readonly SystemOneRequest Request = JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;
    private static readonly Uri Endpoint = new("https://api.test/");

    // Without retries it hands back the inner call's ValueTask itself, so it adds no allocation and no state machine.
    [Fact]
    public void Without_retries_a_completed_listing_is_the_inner_call_and_allocates_nothing()
    {
        var inner = new CompletedApi();
        var operations = new DecisionOperations(inner, "Bearer k", retry: null, static () => false);

        AllocationGate.AssertBudgetValueTask(0, 1000, () => operations.ListModelsAsync("typesafe", Endpoint, CancellationToken.None), "OperationsListModels");
        Assert.True(inner.Calls > 0);
    }

    // With retries, a completed success is still returned without a state machine.
    [Fact]
    public void With_retries_a_completed_listing_allocates_nothing()
    {
        var operations = new DecisionOperations(new CompletedApi(), "Bearer k", Retry(maxRetries: 2), static () => false);

        AllocationGate.AssertBudgetValueTask(0, 1000, () => operations.ListModelsAsync("typesafe", Endpoint, CancellationToken.None), "RetriedListModels");
    }

    [Fact]
    public void A_pending_listing_is_returned_as_is()
    {
        var pending = new TaskCompletionSource<Result<ModelList, DecisionError>>();
        var operations = new DecisionOperations(new PendingApi(pending.Task), "Bearer k", retry: null, static () => false);

        Assert.Same(pending.Task, operations.ListModelsAsync("typesafe", Endpoint, CancellationToken.None).AsTask());
    }

    // The client was disposed after its entry check, so the send threw ObjectDisposedException.
    [Fact]
    public async Task An_attempt_faulted_with_object_disposed_exception_after_disposal_is_disposed_with_the_exception()
    {
        var thrown = new ObjectDisposedException(typeof(HttpClient).FullName);
        var operations = new DecisionOperations(new FaultingApi(thrown), "Bearer k", Retry(maxRetries: 2), static () => true);

        AssertDisposed(await operations.ListModelsAsync("typesafe", Endpoint, CancellationToken.None), thrown);
        AssertDisposed(await operations.EvaluateAsync(Request, "typesafe", Endpoint, CancellationToken.None), thrown);
    }

    [Fact]
    public async Task An_attempt_faulted_with_object_disposed_exception_before_disposal_faults_unchanged()
    {
        var thrown = new ObjectDisposedException("something else");
        var operations = new DecisionOperations(new FaultingApi(thrown), "Bearer k", retry: null, static () => false);

        Assert.Same(thrown, await Assert.ThrowsAsync<ObjectDisposedException>(async () => await operations.ListModelsAsync("typesafe", Endpoint, CancellationToken.None)));
        Assert.Same(thrown, await Assert.ThrowsAsync<ObjectDisposedException>(async () => await operations.EvaluateAsync(Request, "typesafe", Endpoint, CancellationToken.None)));
    }

    [Fact]
    public async Task An_attempt_faulted_with_another_exception_after_disposal_faults_unchanged()
    {
        var thrown = new InvalidOperationException("a bug");
        var operations = new DecisionOperations(new FaultingApi(thrown), "Bearer k", retry: null, static () => true);

        Assert.Same(thrown, await Assert.ThrowsAsync<InvalidOperationException>(async () => await operations.ListModelsAsync("typesafe", Endpoint, CancellationToken.None)));
    }

    [Fact]
    public async Task Each_raw_call_retries_a_transient_failure_and_sends_the_attempt_number()
    {
        var api = new FailingOnceApi();
        var operations = new DecisionOperations(api, "Bearer k", Retry(maxRetries: 1), static () => false);

        Assert.True((await operations.EvaluateAsync(Request, "typesafe", Endpoint, CancellationToken.None)).IsSuccess);
        Assert.True((await operations.ListModelsAsync("typesafe", Endpoint, CancellationToken.None)).IsSuccess);

        Assert.Equal([null, 1, null, 1], api.RetryCounts);
    }

    [Fact]
    public async Task Without_retries_a_transient_failure_is_returned()
    {
        var api = new FailingOnceApi();
        var operations = new DecisionOperations(api, "Bearer k", retry: null, static () => false);

        Assert.Equal(DecisionErrorKind.Overloaded, (await operations.ListModelsAsync("typesafe", Endpoint, CancellationToken.None)).Error.Kind);
        Assert.Equal([null], api.RetryCounts);
    }

    private static DecisionRetry Retry(int maxRetries)
        => new(new DecisionRetryOptions { MaxRetries = maxRetries, InitialBackoff = TimeSpan.FromMilliseconds(1) }, logger: null, TimeProvider.System, static () => false);

    private static void AssertDisposed<T>(Result<T, DecisionError> result, Exception exception)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Disposed, result.Error.Kind);
        Assert.Same(exception, result.Error.Exception);
    }

    // Answers every call synchronously with the same prebuilt result, so the call itself allocates nothing.
    private sealed class CompletedApi : IDecisionApi
    {
        private static readonly Result<SystemOneResponse, DecisionError> Evaluated = Result<SystemOneResponse, DecisionError>.Success(
            JsonSerializer.Deserialize(Fixture.Text("response-noul.json"), DecisionJsonContext.Default.SystemOneResponse)!);
        private static readonly Result<RawJson, DecisionError> Raw = Result<RawJson, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "server"));
        private static readonly Result<ModelList, DecisionError> Models = Result<ModelList, DecisionError>.Success(new ModelList { Models = [] });

        public int Calls { get; private set; }

        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
        {
            Calls++;
            return new(Evaluated);
        }

        public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
        {
            Calls++;
            return new(Raw);
        }

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
        {
            Calls++;
            return new(Models);
        }
    }

    // The first attempt of each call fails as overloaded; the next one succeeds. Records each retry count.
    private sealed class FailingOnceApi : IDecisionApi
    {
        private static readonly DecisionError Overloaded = new(DecisionErrorKind.Overloaded, "overloaded") { StatusCode = 503 };

        public List<int?> RetryCounts { get; } = [];

        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
        {
            RetryCounts.Add(retryCount);
            return new(retryCount is null
                ? Result<SystemOneResponse, DecisionError>.Failure(Overloaded)
                : Result<SystemOneResponse, DecisionError>.Success(
                    JsonSerializer.Deserialize(Fixture.Text("response-noul.json"), DecisionJsonContext.Default.SystemOneResponse)!));
        }

        public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
        {
            RetryCounts.Add(retryCount);
            return new(retryCount is null
                ? Result<ModelList, DecisionError>.Failure(Overloaded)
                : Result<ModelList, DecisionError>.Success(new ModelList { Models = [] }));
        }
    }

    // Every call has already faulted with the exception, as an async method that threw before its first await.
    private sealed class FaultingApi(Exception thrown) : IDecisionApi
    {
        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
            => ValueTask.FromException<Result<SystemOneResponse, DecisionError>>(thrown);

        public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
            => ValueTask.FromException<Result<RawJson, DecisionError>>(thrown);

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
            => ValueTask.FromException<Result<ModelList, DecisionError>>(thrown);
    }

    private sealed class PendingApi(Task<Result<ModelList, DecisionError>> pending) : IDecisionApi
    {
        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
            => new(pending);
    }
}
