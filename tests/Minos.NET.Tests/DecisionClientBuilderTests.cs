using Microsoft.Extensions.DependencyInjection;

namespace Minos.Tests;

public sealed class DecisionClientBuilderTests
{
    private sealed class Tag(IDecisionClient inner, string name, List<string> order) : DelegatingDecisionClient(inner)
    {
        public override ValueTask<ZeroAlloc.Results.Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            order.Add(name);
            return base.EvaluateAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task First_use_is_the_outermost_stage()
    {
        var order = new List<string>();
        using var client = new ClientTestKit.CapturingClient().AsBuilder()
            .Use(inner => new Tag(inner, "outer", order))
            .Use(inner => new Tag(inner, "inner", order))
            .Build();

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.Equal(["outer", "inner"], order);
    }

    [Fact]
    public void Build_without_stages_returns_the_inner_client()
    {
        var inner = new ClientTestKit.CapturingClient();

        Assert.Same(inner, new DecisionClientBuilder(inner).Build());
    }

    [Fact]
    public void Service_provider_overload_receives_the_build_services()
    {
        var services = new ServiceCollection().AddSingleton("from-services").BuildServiceProvider();
        string? seen = null;

        new DecisionClientBuilder(new ClientTestKit.CapturingClient())
            .Use((inner, sp) =>
            {
                seen = sp.GetRequiredService<string>();
                return inner;
            })
            .Build(services);

        Assert.Equal("from-services", seen);
    }

    [Fact]
    public void Build_without_services_uses_an_empty_provider()
    {
        IServiceProvider? seen = null;

        new DecisionClientBuilder(new ClientTestKit.CapturingClient()).Use((inner, sp) =>
        {
            seen = sp;
            return inner;
        }).Build();

        Assert.NotNull(seen);
        Assert.Null(seen!.GetService(typeof(string)));
    }

    [Fact]
    public void Factory_constructor_creates_the_inner_client_from_the_services()
    {
        var inner = new ClientTestKit.CapturingClient();
        var services = new ServiceCollection().AddSingleton<IDecisionClient>(inner).BuildServiceProvider();

        Assert.Same(inner, new DecisionClientBuilder(sp => sp.GetRequiredService<IDecisionClient>()).Build(services));
    }

    [Fact]
    public async Task Delegate_use_wraps_the_call()
    {
        var seen = new List<string>();
        using var client = new ClientTestKit.CapturingClient().AsBuilder()
            .Use(async (request, inner, ct) =>
            {
                seen.Add("before");
                var result = await inner.EvaluateAsync(request, ct);
                seen.Add("after");
                return result;
            })
            .Build();

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["before", "after"], seen);
    }

    [Fact]
    public void A_stage_factory_returning_null_throws()
        => Assert.Throws<InvalidOperationException>(() => new ClientTestKit.CapturingClient().AsBuilder().Use(_ => null!).Build());

    [Fact]
    public void Null_arguments_throw()
    {
        var builder = new ClientTestKit.CapturingClient().AsBuilder();

        Assert.Throws<ArgumentNullException>("stageFactory", () => builder.Use((Func<IDecisionClient, IDecisionClient>)null!));
        Assert.Throws<ArgumentNullException>("stageFactory", () => builder.Use((Func<IDecisionClient, IServiceProvider, IDecisionClient>)null!));
        Assert.Throws<ArgumentNullException>("evaluate", () => builder.Use((Func<DecisionRequest, IDecisionClient, CancellationToken, ValueTask<ZeroAlloc.Results.Result<DecisionResponse, DecisionError>>>)null!));
        Assert.Throws<ArgumentNullException>("innerClient", () => ((IDecisionClient)null!).AsBuilder());
    }
}
