using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace Minos.Transport;

/// <summary>The Jev HTTP API. ZeroAlloc.Rest implements it as <c>DecisionApiClient</c>; ZeroAlloc.Resilience wraps it in <c>IDecisionApiResilienceProxy</c>.</summary>
/// <remarks>
/// The API key travels as a per-call <c>Authorization</c> header so a caller's <see cref="HttpClient"/> is never
/// mutated. Error bodies are read up to 16 KiB. The attribute values of <see cref="RetryAttribute"/> are compile-time
/// defaults only: <see cref="DecisionClient"/> always supplies a runtime <see cref="RetryPolicy"/> built from
/// <see cref="DecisionClientOptions"/>. Each retry sends the attempt number as <c>X-TypeSafe-Retry-Count</c>, absent on
/// the first attempt, as TypeSafe's official SDKs do. A declined exception is rethrown unchanged instead of being
/// wrapped in <see cref="ResilienceException"/>.
/// </remarks>
[ZeroAllocRestClient(MaxErrorBodyBytes = 16384)]
[ErrorMapper(typeof(DecisionErrorMapper))]
[Retry(RetryWhen = nameof(IsTransient), DelayHint = nameof(RetryAfter), RetryOnException = nameof(NeverRetry), RethrowDeclined = true)]
internal interface IDecisionApi
{
    [Post("v1/systemone")]
    ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
        [Body] SystemOneRequest body,
        [Header("Authorization")] string authorization,
        [Header("X-TypeSafe-Retry-Count")] [RetryAttempt] int? retryCount,
        CancellationToken ct);

    /// <summary>
    /// The same endpoint as <see cref="EvaluateAsync"/>, with pre-written UTF-8 JSON in and the raw response body out,
    /// for typed evaluation. The caller owns both <see cref="RawJson"/> values: the retry proxy sends the same
    /// <paramref name="body"/> on every attempt, so it must stay undisposed until the call completes.
    /// </summary>
    [Post("v1/systemone")]
    [Serializer(typeof(DecisionRawSerializer))]
    ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(
        [Body] RawJson body,
        [Header("Authorization")] string authorization,
        [Header("X-TypeSafe-Retry-Count")] [RetryAttempt] int? retryCount,
        CancellationToken ct);

    /// <summary>
    /// The protocol-neutral attempt: POSTs pre-written UTF-8 JSON to <paramref name="path"/>, relative to the base address,
    /// and returns the raw response body. <c>DecisionTransport</c> sends one attempt through it, passing the protocol's
    /// <see cref="Protocols.IDecisionProtocol.EndpointPath"/>. The caller owns both <see cref="RawJson"/> values.
    /// </summary>
    /// <remarks>
    /// <c>{**path}</c> escapes each segment and keeps the <c>/</c> between them, so <c>v1/systemone</c> is sent as is.
    /// The path must not start with <c>/</c>, which would make it root-relative and drop the base address's own path.
    /// The transport passes the retry count itself, from <see cref="DecisionRequest.RetryAttempt"/>, so it carries no
    /// <see cref="RetryAttemptAttribute"/>.
    /// </remarks>
    [Post("{**path}")]
    [Serializer(typeof(DecisionRawSerializer))]
    ValueTask<Result<RawJson, DecisionError>> SendAsync(
        [Body] RawJson body,
        string path,
        [Header("Authorization")] string authorization,
        [Header("X-TypeSafe-Retry-Count")] int? retryCount,
        CancellationToken ct);

    [Get("v1/models")]
    ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(
        [Header("Authorization")] string authorization,
        [Header("X-TypeSafe-Retry-Count")] [RetryAttempt] int? retryCount,
        CancellationToken ct);

    /// <summary>Whether a failure is worth another attempt: rate limiting, overload, server errors, 408, network failures and time-outs.</summary>
    /// <remarks>
    /// Never <see cref="DecisionErrorKind.Disposed"/>: <see cref="DecisionErrorMapper"/> reports an attempt torn down by disposing
    /// the client as that kind, not as a time-out or network failure, so a disposed client is never retried.
    /// </remarks>
    static bool IsTransient(DecisionError error)
        => error.Kind is DecisionErrorKind.RateLimited or DecisionErrorKind.Overloaded or DecisionErrorKind.Server
            or DecisionErrorKind.Network or DecisionErrorKind.Timeout
            || error.StatusCode == 408;

    /// <summary>The wait the server asked for, which replaces the backoff for the next attempt.</summary>
    static TimeSpan? RetryAfter(DecisionError error) => error.RetryAfter;

    /// <summary>
    /// Thrown exceptions are programming errors, never transient: ZeroAlloc.Rest returns every transport failure as a
    /// failed Result.
    /// </summary>
    static bool NeverRetry(Exception exception) => false;
}
