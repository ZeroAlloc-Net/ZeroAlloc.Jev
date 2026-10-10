using System.Net;
using Microsoft.Extensions.DependencyInjection;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary>The <see cref="DecisionClientServiceBuilder"/> that <c>AddDecisionClient</c> returns, and the stages added through it.</summary>
public sealed class DecisionClientServiceBuilderTests
{
    [Fact]
    public void Returns_a_builder_naming_the_registration()
    {
        var services = new ServiceCollection();

        var builder = services.AddDecisionClient(o => o.ApiKey = "k");
        var keyed = services.AddDecisionClient("second", o => o.ApiKey = "k");

        Assert.Null(builder.Name);
        Assert.Equal("second", keyed.Name);
        Assert.NotNull(builder.HttpClient);
    }

    [Fact]
    public void Stages_wrap_the_registered_client()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient(o => o.ApiKey = "k").Use(inner => new Marker(inner));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();

        Assert.IsType<Marker>(client);
        Assert.NotNull(client.GetService<DecisionClient>());
    }

    [Fact]
    public void A_repeat_call_returns_the_same_builder()
    {
        var services = new ServiceCollection();

        var first = services.AddDecisionClient(o => o.ApiKey = "k");
        var second = services.AddDecisionClient();

        Assert.Same(first, second);
    }

    [Fact]
    public void Keyed_registrations_have_their_own_builders()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("a", o => o.ApiKey = "k").Use(inner => new Marker(inner));
        services.AddDecisionClient("b", o => o.ApiKey = "k");

        using var provider = services.BuildServiceProvider();

        Assert.IsType<Marker>(provider.GetRequiredKeyedService<IDecisionClient>("a"));
        Assert.IsNotType<Marker>(provider.GetRequiredKeyedService<IDecisionClient>("b"));
    }

    [Fact]
    public async Task Http_client_handlers_still_apply()
    {
        var handler = Noul();
        var seen = new List<Uri?>();
        var services = new ServiceCollection();
        services
            .AddDecisionClient(Options("http://default.local/"))
            .HttpClient
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddHttpMessageHandler(() => new RecordingHandler(seen));
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Request());

        Assert.Equal([new Uri("http://default.local/v1/systemone")], seen);
    }

    [Fact]
    public async Task Configured_retry_settings_reach_use_retries_with_the_standard_pipeline_off()
    {
        var handler = StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
        var services = new ServiceCollection();
        var builder = services.AddDecisionClient(o =>
        {
            o.ApiKey = "k";
            o.BaseAddress = new Uri("http://default.local/");
            o.UseStandardPipeline = false;
            o.MaxRetries = 5;
            o.InitialBackoff = TimeSpan.FromMilliseconds(1);
        });
        builder.UseRetries();
        builder.HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();

        Assert.NotNull(client.GetService<RetryingDecisionClient>());
        Assert.Equal(5, client.GetService<DecisionRetryOptions>()!.MaxRetries);

        var set = QuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out _).Build().Value;
        var result = await client.EvaluateAsync(set, "Help!");

        Assert.True(result.IsFailure);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public void An_app_registered_client_still_wins()
    {
        var services = new ServiceCollection();
        var mine = new Marker(new DecisionClient(new DecisionClientOptions { ApiKey = "k" }));
        services.AddSingleton<IDecisionClient>(mine);
        services.AddDecisionClient(o => o.ApiKey = "k");

        using var provider = services.BuildServiceProvider();

        Assert.Same(mine, provider.GetRequiredService<IDecisionClient>());
    }

    private sealed class Marker(IDecisionClient inner) : DelegatingDecisionClient(inner);

    private sealed class RecordingHandler(List<Uri?> seen) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen.Add(request.RequestUri);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
