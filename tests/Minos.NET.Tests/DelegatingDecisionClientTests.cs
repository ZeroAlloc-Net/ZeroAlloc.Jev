namespace Minos.Tests;

public sealed class DelegatingDecisionClientTests
{
    private sealed class PassThrough(IDecisionClient inner) : DelegatingDecisionClient(inner);

    private sealed class Tracking : IDecisionClient
    {
        public bool Disposed { get; private set; }

        public ValueTask<ZeroAlloc.Results.Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(string) ? "inner" : null;

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task Forwards_evaluate_to_the_inner_client()
    {
        var inner = new ClientTestKit.CapturingClient();
        using var stage = new PassThrough(inner);
        var request = new DecisionRequest(QuestionSets.UrgencyDefinition(), "s");

        var result = await stage.EvaluateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Same(request, inner.OnlyRequest());
    }

    [Fact]
    public void GetService_returns_the_stage_then_asks_the_inner_client()
    {
        using var stage = new PassThrough(new Tracking());

        Assert.Same(stage, stage.GetService(typeof(PassThrough)));
        Assert.Same(stage, stage.GetService(typeof(DelegatingDecisionClient)));
        Assert.Equal("inner", stage.GetService(typeof(string)));
        Assert.Null(stage.GetService(typeof(PassThrough), "key"));
    }

    [Fact]
    public void Dispose_disposes_the_inner_client()
    {
        var inner = new Tracking();
        var stage = new PassThrough(inner);

        stage.Dispose();

        Assert.True(inner.Disposed);
    }

    [Fact]
    public void Null_inner_client_throws()
        => Assert.Throws<ArgumentNullException>("innerClient", () => new PassThrough(null!));

    [Fact]
    public async Task Pass_through_over_a_completed_call_returns_the_same_task()
    {
        var inner = new ClientTestKit.CapturingClient();
        using var stage = new PassThrough(inner);

        var call = stage.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.True(call.IsCompletedSuccessfully);
        await call;
    }
}
