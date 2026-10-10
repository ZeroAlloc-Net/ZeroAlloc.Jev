using System.Text.Json;
using Minos.Serialization;
using Minos.Transport;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace Minos.Tests;

/// <summary>The decorator that keeps an attempt from being sent once the owning client is disposed.</summary>
public sealed class DisposalGuardDecisionApiTests
{
    private static readonly SystemOneRequest Request = JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;

    // Before disposal it hands back the inner call's ValueTask itself, so it adds no allocation and no state machine.
    [Fact]
    public void BeforeDisposal_EveryCall_IsTheInnerCall_AndAllocatesNothing()
    {
        var inner = new CompletedApi();
        var guard = new DisposalGuardDecisionApi(inner, static () => false);
        using var body = RawJson.Create(System.Buffers.ArrayPool<byte>.Shared, 16);

        AllocationGate.AssertBudgetValueTask(0, 1000, () => guard.EvaluateAsync(Request, "Bearer k", null, CancellationToken.None), "GuardEvaluate");
        AllocationGate.AssertBudgetValueTask(0, 1000, () => guard.EvaluateRawAsync(body, "Bearer k", null, CancellationToken.None), "GuardEvaluateRaw");
        AllocationGate.AssertBudgetValueTask(0, 1000, () => guard.SendAsync(body, "v1/systemone", "Bearer k", null, CancellationToken.None), "GuardSend");
        AllocationGate.AssertBudgetValueTask(0, 1000, () => guard.ListModelsAsync("Bearer k", null, CancellationToken.None), "GuardListModels");
        Assert.True(inner.Calls > 0);
    }

    [Fact]
    public void BeforeDisposal_APendingCall_IsReturnedAsIs()
    {
        var pending = new TaskCompletionSource<Result<ModelList, DecisionError>>();
        var guard = new DisposalGuardDecisionApi(new PendingApi(pending.Task), static () => false);

        Assert.Same(pending.Task, guard.ListModelsAsync("Bearer k", 1, CancellationToken.None).AsTask());
    }

    [Fact]
    public void AfterDisposal_EveryCall_IsDisposed_WithoutCallingTheInnerApi()
    {
        var inner = new CompletedApi();
        var guard = new DisposalGuardDecisionApi(inner, static () => true);
        using var body = RawJson.Create(System.Buffers.ArrayPool<byte>.Shared, 16);

        AssertDisposed(guard.EvaluateAsync(Request, "Bearer k", 1, CancellationToken.None));
        AssertDisposed(guard.EvaluateRawAsync(body, "Bearer k", 1, CancellationToken.None));
        AssertDisposed(guard.SendAsync(body, "v1/systemone", "Bearer k", 1, CancellationToken.None));
        AssertDisposed(guard.ListModelsAsync("Bearer k", 1, CancellationToken.None));
        Assert.Equal(0, inner.Calls);
    }

    // The client was disposed after the guard read the flag, so the send threw ObjectDisposedException.
    [Fact]
    public void AnAttemptFaultedWithObjectDisposedException_AfterDisposal_IsDisposed_WithTheException()
    {
        var thrown = new ObjectDisposedException(typeof(HttpClient).FullName);
        var reads = 0;
        var guard = new DisposalGuardDecisionApi(new FaultingApi(thrown), () => ++reads > 1);

        var call = guard.ListModelsAsync("Bearer k", null, CancellationToken.None);

        AssertDisposed(call, thrown);
    }

    [Fact]
    public async Task AnAttemptFaultedWithObjectDisposedException_BeforeDisposal_FaultsUnchanged()
    {
        var thrown = new ObjectDisposedException("something else");
        var guard = new DisposalGuardDecisionApi(new FaultingApi(thrown), static () => false);

        Assert.Same(thrown, await Assert.ThrowsAsync<ObjectDisposedException>(async () => await guard.ListModelsAsync("Bearer k", null, CancellationToken.None)));
    }

    [Fact]
    public async Task AnAttemptFaultedWithAnotherException_AfterDisposal_FaultsUnchanged()
    {
        var thrown = new InvalidOperationException("a bug");
        var reads = 0;
        var guard = new DisposalGuardDecisionApi(new FaultingApi(thrown), () => ++reads > 1);

        Assert.Same(thrown, await Assert.ThrowsAsync<InvalidOperationException>(async () => await guard.ListModelsAsync("Bearer k", null, CancellationToken.None)));
    }

    private static void AssertDisposed<T>(ValueTask<Result<T, DecisionError>> call, Exception? exception = null)
    {
        Assert.True(call.IsCompletedSuccessfully);
        var error = call.Result.Error;
        Assert.Equal(DecisionErrorKind.Disposed, error.Kind);
        Assert.Same(exception, error.Exception);
        Assert.False(IDecisionApi.IsTransient(error));
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

        public ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
        {
            Calls++;
            return new(Raw);
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

    // Every call has already faulted with the exception, as an async method that threw before its first await.
    private sealed class FaultingApi(Exception thrown) : IDecisionApi
    {
        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
            => ValueTask.FromException<Result<SystemOneResponse, DecisionError>>(thrown);

        public ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
            => ValueTask.FromException<Result<RawJson, DecisionError>>(thrown);

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

        public ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
            => new(pending);
    }
}
