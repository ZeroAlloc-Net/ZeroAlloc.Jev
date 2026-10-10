using ZeroAlloc.Results;

namespace Minos.Transport;

/// <summary>
/// Sits between the ZeroAlloc.Resilience proxy and the transport of every <see cref="DecisionClient"/>, and answers every
/// attempt that starts after the client was disposed with a <see cref="DecisionErrorKind.Disposed"/> failure instead of
/// sending it, so a disposed client never retries.
/// </summary>
/// <remarks>
/// <para>
/// For an owned <see cref="HttpClient"/>, <see cref="DecisionErrorMapper"/> already maps an attempt torn down by the
/// disposal to <see cref="DecisionErrorKind.Disposed"/>, which <see cref="IDecisionApi.IsTransient"/> never retries. This covers
/// the other ways a disposal meets a call in flight:
/// </para>
/// <list type="bullet">
/// <item><description>
/// An attempt fails transiently on its own, and the disposal lands before the proxy sends the retry. An owned
/// <see cref="HttpClient"/> would throw <see cref="ObjectDisposedException"/> for that retry; a borrowed one would send it
/// from a disposed client. Neither happens: the retry is never sent.
/// </description></item>
/// <item><description>
/// The disposal lands after this guard read the flag but before the owned <see cref="HttpClient"/> or its handler
/// checked whether it was disposed, so the send throws <see cref="ObjectDisposedException"/>. ZeroAlloc.Rest rethrows
/// that exception unmapped. Once the flag is set, this guard turns it into the same <see cref="DecisionErrorKind.Disposed"/>
/// failure; before then, the exception is not the client's disposal and propagates unchanged. Both checks throw before
/// the send's first asynchronous step, so the attempt's <see cref="ValueTask{TResult}"/> is already faulted when the
/// transport returns it, and this guard inspects it in place.
/// </description></item>
/// </list>
/// <para>
/// A borrowed <see cref="HttpClient"/> is never disposed by the client, so its attempt already in flight is not torn
/// down and keeps its own result; only the retries after it are refused. Before disposal this returns the inner call's
/// <see cref="ValueTask{TResult}"/> itself unless it has already faulted, so it adds no allocation and no state machine.
/// </para>
/// </remarks>
/// <param name="inner">The next step toward the transport.</param>
/// <param name="disposed">Reads whether the client has been disposed.</param>
internal sealed class DisposalGuardDecisionApi(IDecisionApi inner, Func<bool> disposed) : IDecisionApi
{
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
        SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
        => disposed() ? DisposedFailure<SystemOneResponse>(exception: null) : Checked(inner.EvaluateAsync(body, authorization, retryCount, ct));

    public ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
        => disposed() ? DisposedFailure<RawJson>(exception: null) : Checked(inner.EvaluateRawAsync(body, authorization, retryCount, ct));

    public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
        => disposed() ? DisposedFailure<RawJson>(exception: null) : Checked(inner.SendAsync(body, path, authorization, retryCount, ct));

    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
        => disposed() ? DisposedFailure<ModelList>(exception: null) : Checked(inner.ListModelsAsync(authorization, retryCount, ct));

    private static ValueTask<Result<T, DecisionError>> DisposedFailure<T>(ObjectDisposedException? exception)
        => new(Result<T, DecisionError>.Failure(DecisionErrorMapper.Disposed(exception)));

    // A pending or successful attempt is returned as is. Only an attempt that has already faulted is looked at: one
    // that faulted with ObjectDisposedException after the client was disposed becomes Disposed, and any other fault is
    // handed back unchanged.
    private ValueTask<Result<T, DecisionError>> Checked<T>(ValueTask<Result<T, DecisionError>> attempt)
    {
        if (!attempt.IsFaulted)
        {
            return attempt;
        }

        var faulted = attempt.AsTask();
        return faulted.Exception?.InnerException is ObjectDisposedException exception && disposed()
            ? DisposedFailure<T>(exception)
            : new ValueTask<Result<T, DecisionError>>(faulted);
    }
}
