namespace Minos.Docs.Tests;

#region TestingYourCode_Fake
using Minos;
using ZeroAlloc.Results;

// A fake implements EvaluateAsync, GetService and Dispose. The typed calls are extension methods over EvaluateAsync.
public sealed class FakeDecision(Result<DecisionResponse, DecisionError> reply) : IDecisionClient
{
    private readonly List<DecisionRequest> _requests = [];

    // Every request the code under test sent, so a test can check what was asked.
    public IReadOnlyList<DecisionRequest> Requests => _requests;

    // A reply that answers both questions of TriageQuestions, in the order the set declares them.
    public static FakeDecision Answering(double urgent, TriageDesk desk, double deskConfidence)
    {
        // A Choice answer gives one probability per option, in the order the enum declares them.
        var desks = Enum.GetValues<TriageDesk>();
        var probabilities = new double[desks.Length];
        for (var i = 0; i < desks.Length; i++)
        {
            probabilities[i] = desks[i] == desk ? 0.7 : 0.15;
        }

        return new FakeDecision(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
            TriageQuestions.Definition,
            [QuestionAnswer.Noul(urgent), QuestionAnswer.Choice(Array.IndexOf(desks, desk), deskConfidence, probabilities)],
            model: "fake")));
    }

    // A reply that is a failure, as a rejected key or a network error would be.
    public static FakeDecision Failing(DecisionErrorKind kind)
        => new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, "The fake failed on purpose.")));

    // A busy service: the kind and message are the constructor's, the rest are init properties.
    public static FakeDecision Overloaded(TimeSpan retryAfter)
        => new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Overloaded, "The fake is busy on purpose.")
        {
            StatusCode = 503,
            RetryAfter = retryAfter,
        }));

    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Add(request);
        return ValueTask.FromResult(reply);
    }

    // The fake offers no services, such as DecisionClientMetadata.
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
#endregion
