using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.Telemetry;

/// <summary>
/// The client's two raw System One operations over the transport. It holds no telemetry code: <see cref="DecisionClient"/>
/// calls it through the generated <c>DecisionOperationsInstrumented</c>. The provider and endpoint are for the proxy's tags
/// and unused here.
/// </summary>
/// <param name="api">The transport.</param>
/// <param name="authorization">The <c>Authorization</c> header value.</param>
/// <param name="retry">The retry loop each call runs through; <see langword="null"/> sends one attempt per call.</param>
/// <param name="disposed">Reads whether the client has been disposed.</param>
internal sealed class DecisionOperations(IDecisionApi api, string authorization, DecisionRetry? retry, Func<bool> disposed) : IDecisionOperations
{
    private static readonly Func<(IDecisionApi Api, SystemOneRequest Request, string Authorization), int, CancellationToken, ValueTask<Result<SystemOneResponse, DecisionError>>> EvaluateAttempt
        = static (state, retryCount, ct) => state.Api.EvaluateAsync(state.Request, state.Authorization, retryCount, ct);

    private static readonly Func<(IDecisionApi Api, string Authorization), int, CancellationToken, ValueTask<Result<ModelList, DecisionError>>> ListModelsAttempt
        = static (state, retryCount, ct) => state.Api.ListModelsAsync(state.Authorization, retryCount, ct);

    // ZeroAlloc.Rest 3.0 itself rejects an empty or null success body as a Deserialization error before this runs, since
    // SystemOneResponse is non-nullable in IDecisionApi; only a null value nested inside a non-null response, such as an
    // answer, still needs to be caught here, once the retries are done.
    public async ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
        SystemOneRequest request, string provider, Uri endpoint, CancellationToken ct)
    {
        var result = await Retried(
            api.EvaluateAsync(request, authorization, retryCount: null, ct), (api, request, authorization), EvaluateAttempt, ct).ConfigureAwait(false);

        if (result.IsSuccess && HasNullAnswer(result.Value))
        {
            return Result<SystemOneResponse, DecisionError>.Failure(Unreadable("The response contains a null answer."));
        }

        return result;
    }

    // ZeroAlloc.Rest 3.0 itself rejects an empty or null success body as a Deserialization error before this runs,
    // since ModelList is non-nullable in IDecisionApi, so no null check remains needed here.
    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string provider, Uri endpoint, CancellationToken ct)
        => Retried(api.ListModelsAsync(authorization, retryCount: null, ct), (api, authorization), ListModelsAttempt, ct);

    // Without a retry loop the first call is the only one; the retry loop guards its own attempts against a racing Dispose.
    private ValueTask<Result<T, DecisionError>> Retried<TState, T>(
        ValueTask<Result<T, DecisionError>> first,
        TState state,
        Func<TState, int, CancellationToken, ValueTask<Result<T, DecisionError>>> attempt,
        CancellationToken ct)
        => retry is null ? Guarded(first) : retry.RunAsync(Guarded(first), state, attempt, ct);

    // Dispose landed after the client's entry check but before the send checked it, so the send threw
    // ObjectDisposedException, which ZeroAlloc.Rest rethrows unmapped before the attempt's first asynchronous step. Once
    // the flag is set that becomes a Disposed failure; any other fault, or one before Dispose, is handed back unchanged. A
    // pending or successful attempt is returned as is.
    private ValueTask<Result<T, DecisionError>> Guarded<T>(ValueTask<Result<T, DecisionError>> attempt)
    {
        if (!attempt.IsFaulted)
        {
            return attempt;
        }

        var faulted = attempt.AsTask();
        return faulted.Exception?.InnerException is ObjectDisposedException exception && disposed()
            ? new(Result<T, DecisionError>.Failure(DecisionErrorMapper.Disposed(exception)))
            : new ValueTask<Result<T, DecisionError>>(faulted);
    }

    // System.Text.Json does not apply nullable annotations to dictionary values, so a null answer can arrive.
    private static bool HasNullAnswer(SystemOneResponse response)
    {
        foreach (var answer in response.Answers.Values)
        {
            if (answer is null)
            {
                return true;
            }
        }

        return false;
    }

    // statusCode 200 is a placeholder: ZeroAlloc.Rest's generated client does not expose the real status of a
    // successful response that this client itself then rejects as unreadable, so 200 is kept only because that is
    // the status that let the response through in the first place.
    private static DecisionError Unreadable(string message) => new(DecisionErrorKind.InvalidResponse, message) { StatusCode = 200 };
}
