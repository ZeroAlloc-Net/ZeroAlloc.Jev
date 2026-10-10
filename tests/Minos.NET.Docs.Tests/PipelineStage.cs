namespace Minos.Docs.Tests;

#region Pipeline_Stage
using Minos;
using ZeroAlloc.Results;

// Where the audit records go. An application would write them to a database or a log of its own.
public interface IAuditLog
{
    void Record(QuestionSetDefinition definition, Result<DecisionResponse, DecisionError> result);
}

// A stage derives from DelegatingDecisionClient and overrides EvaluateAsync. base.EvaluateAsync calls the client inside.
public sealed class AuditStage(IDecisionClient inner, IAuditLog log) : DelegatingDecisionClient(inner)
{
    public override async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(
        DecisionRequest request, CancellationToken cancellationToken = default)
    {
        var result = await base.EvaluateAsync(request, cancellationToken);
        log.Record(request.Definition, result);
        return result;
    }
}
#endregion
