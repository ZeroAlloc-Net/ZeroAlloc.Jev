using ZeroAlloc.Results;

namespace Minos;

/// <summary>The stage <see cref="DecisionClientBuilder.Use(Func{DecisionRequest, IDecisionClient, CancellationToken, ValueTask{Result{DecisionResponse, DecisionError}}})"/> adds.</summary>
internal sealed class AnonymousDelegatingDecisionClient(
    IDecisionClient innerClient,
    Func<DecisionRequest, IDecisionClient, CancellationToken, ValueTask<Result<DecisionResponse, DecisionError>>> evaluate)
    : DelegatingDecisionClient(innerClient)
{
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        => evaluate(request, InnerClient, cancellationToken);
}
