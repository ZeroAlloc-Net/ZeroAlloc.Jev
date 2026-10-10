namespace Minos.Docs.Tests;

#region Pipeline_Fake
using Minos;
using ZeroAlloc.Results;

// What a fake implements: one EvaluateAsync over a DecisionRequest, GetService and Dispose.
public sealed class AlwaysUrgent : IDecisionClient
{
    // One Noul answer of 0.9, for a set of one Noul question such as PipelineCheck.
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Result<DecisionResponse, DecisionError>.Success(
            new DecisionResponse(request.Definition, [QuestionAnswer.Noul(0.9)])));

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
#endregion
