using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace Minos.Transport;

/// <summary>The Jev HTTP API. ZeroAlloc.Rest implements it as <c>DecisionApiClient</c>.</summary>
/// <remarks>
/// The API key travels as a per-call <c>Authorization</c> header so a caller's <see cref="HttpClient"/> is never
/// mutated. Error bodies are read up to 16 KiB. Each method sends one attempt: retries are the pipeline's job, through
/// <see cref="RetryingDecisionClient"/> for the neutral call and the shared retry loop for the raw ones. A retry passes
/// its attempt number, which is sent as <c>X-TypeSafe-Retry-Count</c>, absent on the first attempt, as TypeSafe's official
/// SDKs do.
/// </remarks>
[ZeroAllocRestClient(MaxErrorBodyBytes = 16384)]
[ErrorMapper(typeof(DecisionErrorMapper))]
internal interface IDecisionApi
{
    [Post("v1/systemone")]
    ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
        [Body] SystemOneRequest body,
        [Header("Authorization")] string authorization,
        [Header("X-TypeSafe-Retry-Count")] int? retryCount,
        CancellationToken ct);

    /// <summary>
    /// The protocol-neutral attempt: POSTs pre-written UTF-8 JSON to <paramref name="path"/>, relative to the base address,
    /// and returns the raw response body. <see cref="DecisionTransport"/> sends one attempt through it, passing the protocol's
    /// <see cref="Protocols.IDecisionProtocol.EndpointPath"/>. The caller owns both <see cref="RawJson"/> values.
    /// </summary>
    /// <remarks>
    /// <c>{**path}</c> escapes each segment and keeps the <c>/</c> between them, so <c>v1/systemone</c> is sent as is.
    /// The path must not start with <c>/</c>, which would make it root-relative and drop the base address's own path.
    /// The transport passes the retry count from <see cref="DecisionRequest.RetryAttempt"/>.
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
        [Header("X-TypeSafe-Retry-Count")] int? retryCount,
        CancellationToken ct);
}
