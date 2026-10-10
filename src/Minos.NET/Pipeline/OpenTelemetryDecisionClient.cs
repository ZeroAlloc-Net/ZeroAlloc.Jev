using Minos.Telemetry;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>A stage that records one CLIENT span and one set of GenAI and <c>minos.*</c> metrics per call, retries included when it wraps the retry stage.</summary>
/// <remarks>
/// Spans and metrics come from the <c>Minos</c> source and meter. The provider and <c>server.*</c> tags come from the inner
/// client's <see cref="DecisionClientMetadata"/>; without it the provider is <c>unknown</c> and the server tags are left
/// out. Tags never carry the state, the questions or the answers. With nothing listening, it returns the inner call itself.
/// </remarks>
public sealed class OpenTelemetryDecisionClient : DelegatingDecisionClient
{
    private const string Unknown = "unknown";

    private readonly DecisionEvaluationInstrumented _evaluation;
    private readonly string _provider;
    private readonly Uri? _endpoint;
    private readonly string _defaultModel;

    /// <summary>Initializes a new instance of the <see cref="OpenTelemetryDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client to trace.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    public OpenTelemetryDecisionClient(IDecisionClient innerClient)
        : base(innerClient)
    {
        _evaluation = new DecisionEvaluationInstrumented(new DecisionEvaluation(innerClient));
        var metadata = innerClient.GetService<DecisionClientMetadata>();
        _provider = metadata?.ProviderName ?? Unknown;
        _endpoint = metadata?.Endpoint;
        _defaultModel = metadata?.DefaultModel ?? Unknown;
    }

    /// <inheritdoc />
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _evaluation.EvaluateAsync(request, request.Model ?? _defaultModel, _provider, _endpoint, cancellationToken);
    }
}
