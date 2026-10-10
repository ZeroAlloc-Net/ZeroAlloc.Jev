using ZeroAlloc.Results;

namespace Minos.Telemetry;

/// <summary>The uninstrumented call the telemetry stage's generated proxy wraps.</summary>
internal sealed class DecisionEvaluation(IDecisionClient inner) : IDecisionEvaluation
{
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, string model, string provider, Uri? endpoint, CancellationToken ct)
        => inner.EvaluateAsync(request, ct);
}
