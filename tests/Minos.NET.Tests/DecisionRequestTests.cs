namespace Minos.Tests;

public sealed class DecisionRequestTests
{
    private static readonly QuestionSetDefinition Definition = QuestionSets.UrgencyDefinition();

    [Fact]
    public void Defaults_have_no_model_and_attempt_zero()
    {
        var request = new DecisionRequest(Definition, "a ticket");

        Assert.Same(Definition, request.Definition);
        Assert.Equal(DecisionContent.FromString("a ticket"), request.State);
        Assert.Null(request.Model);
        Assert.Equal(0, request.RetryAttempt);
    }

    [Fact]
    public void With_copies_and_sets_the_attempt()
    {
        var request = new DecisionRequest(Definition, "a ticket") { Model = "minos-x" };

        var retry = request with { RetryAttempt = 2 };

        Assert.NotSame(request, retry);
        Assert.Equal(2, retry.RetryAttempt);
        Assert.Equal("minos-x", retry.Model);
        Assert.Equal(0, request.RetryAttempt);
    }

    [Fact]
    public void Null_definition_throws()
        => Assert.Throws<ArgumentNullException>("Definition", () => new DecisionRequest(null!, "a ticket"));

    [Fact]
    public void Default_state_throws()
        => Assert.Throws<ArgumentException>("State", () => new DecisionRequest(Definition, default));

    [Fact]
    public void Negative_attempt_throws()
        => Assert.Throws<ArgumentOutOfRangeException>("value", () => new DecisionRequest(Definition, "s") { RetryAttempt = -1 });
}
