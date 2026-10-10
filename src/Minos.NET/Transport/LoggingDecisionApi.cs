using Microsoft.Extensions.Logging;
using ZeroAlloc.Resilience;
using ZeroAlloc.Results;

namespace Minos.Transport;

/// <summary>
/// Sits between the ZeroAlloc.Resilience proxy and the ZeroAlloc.Rest transport, so it sees every attempt, and logs
/// <see cref="DecisionLog.AttemptRetrying"/> for each failed attempt the proxy will retry.
/// </summary>
/// <remarks>
/// <para>
/// The proxy passes <see langword="null"/> as the retry count on the first attempt, and the attempt index, 1 and up, on
/// each retry; it also sends that index as <c>X-TypeSafe-Retry-Count</c>. It retries a result that
/// <see cref="IDecisionApi.IsTransient"/> accepts unless the attempt's index is <c>MaxAttempts - 1</c>, and it never
/// retries a thrown exception. This decorator applies the same two tests to the same <see cref="RetryPolicy"/>.
/// If the caller cancels during the backoff, the retry it logged does not happen. Nor does it when the client is
/// disposed before the retry starts: <see cref="DisposalGuardDecisionApi"/> refuses the retry, and the call returns
/// <see cref="DecisionErrorKind.Disposed"/>.
/// </para>
/// <para>
/// With <see cref="LogLevel.Warning"/> disabled it returns the inner call's <see cref="ValueTask{TResult}"/> itself,
/// and a call that completed synchronously is inspected in place, so neither adds a state machine.
/// </para>
/// </remarks>
/// <param name="inner">The transport.</param>
/// <param name="logger">The client's logger.</param>
/// <param name="retry">The policy the proxy retries with.</param>
internal sealed class LoggingDecisionApi(IDecisionApi inner, ILogger logger, RetryPolicy retry) : IDecisionApi
{
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
        SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
        => Observe(inner.EvaluateAsync(body, authorization, retryCount, ct), retryCount);

    public ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
        => Observe(inner.EvaluateRawAsync(body, authorization, retryCount, ct), retryCount);

    public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
        => Observe(inner.SendAsync(body, path, authorization, retryCount, ct), retryCount);

    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
        => Observe(inner.ListModelsAsync(authorization, retryCount, ct), retryCount);

    private ValueTask<Result<T, DecisionError>> Observe<T>(ValueTask<Result<T, DecisionError>> attempt, int? retryCount)
    {
        if (!logger.IsEnabled(LogLevel.Warning))
        {
            return attempt;
        }

        if (attempt.IsCompletedSuccessfully)
        {
            var result = attempt.Result;
            LogIfRetrying(result, retryCount);
            return new ValueTask<Result<T, DecisionError>>(result);
        }

        return ObserveAsync(attempt, retryCount);
    }

    private async ValueTask<Result<T, DecisionError>> ObserveAsync<T>(ValueTask<Result<T, DecisionError>> attempt, int? retryCount)
    {
        var result = await attempt.ConfigureAwait(false);
        LogIfRetrying(result, retryCount);
        return result;
    }

    private void LogIfRetrying<T>(Result<T, DecisionError> result, int? retryCount)
    {
        var index = retryCount ?? 0;
        if (result.IsFailure && IDecisionApi.IsTransient(result.Error) && index < retry.MaxAttempts - 1)
        {
            DecisionLog.AttemptRetrying(logger, index + 1, result.Error.Kind, result.Error.StatusCode, result.Error.RetryAfter);
        }
    }
}
