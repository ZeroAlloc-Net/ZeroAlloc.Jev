using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary>Keyed clients from <c>AddDecisionClient(name, ...)</c>, each with its own options and <see cref="HttpClient"/>.</summary>
public sealed class KeyedDecisionClientTests
{
    [Fact]
    public void KeyedClient_IsOneSingleton_AndNotTheDefault()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient(Options("http://default.local/"));
        services.AddDecisionClient("openrouter", Options("http://keyed.local/"));
        using var provider = services.BuildServiceProvider();

        var keyed = provider.GetRequiredKeyedService<IDecisionClient>("openrouter");

        Assert.IsType<DecisionClient>(keyed);
        Assert.Same(keyed, provider.GetRequiredKeyedService<IDecisionClient>("openrouter"));
        Assert.NotSame(provider.GetRequiredService<IDecisionClient>(), keyed);
        Assert.Null(provider.GetKeyedService<IDecisionClient>("typesafe"));
    }

    [Fact]
    public async Task DefaultAndKeyedClients_KeepTheirOwnHttpClient()
    {
        var defaultHandler = Noul();
        var keyedHandler = Noul();
        var services = new ServiceCollection();
        services
            .AddDecisionClient(Options("http://default.local/", apiKey: "default-key"))
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => defaultHandler);
        services
            .AddDecisionClient("openrouter", Options("http://keyed.local/api/", apiKey: "keyed-key"))
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => keyedHandler);
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Request());
        _ = await provider.GetRequiredKeyedService<IDecisionClient>("openrouter").GetService<DecisionClient>()!.EvaluateAsync(Request());

        var defaultRequest = OnlyRequest(defaultHandler);
        Assert.Equal(new Uri("http://default.local/v1/systemone"), defaultRequest.Uri);
        Assert.Equal("Bearer default-key", defaultRequest.Authorization);
        var keyedRequest = OnlyRequest(keyedHandler);
        Assert.Equal(new Uri("http://keyed.local/api/v1/systemone"), keyedRequest.Uri);
        Assert.Equal("Bearer keyed-key", keyedRequest.Authorization);
    }

    [Fact]
    public async Task TwoKeyedClients_HaveTheirOwnOptionsAndHttpClients()
    {
        var typesafeHandler = Noul();
        var openRouterHandler = Noul();
        var services = new ServiceCollection();
        services
            .AddDecisionClient("typesafe", options =>
            {
                options.ApiKey = "typesafe-key";
                options.BaseAddress = new Uri("http://typesafe.local/");
                options.Model = "jev-typesafe";
                options.Timeout = TimeSpan.FromSeconds(7);
                options.MaxRetries = 0;
            })
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => typesafeHandler);
        services
            .AddDecisionClient("openrouter", options =>
            {
                options.Provider = DecisionProvider.OpenRouter;
                options.ApiKey = "openrouter-key";
                options.BaseAddress = new Uri("http://openrouter.local/api/");
                options.Model = "jev-openrouter";
                options.Timeout = TimeSpan.FromSeconds(9);
                options.MaxRetries = 0;
            })
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => openRouterHandler);
        using var provider = services.BuildServiceProvider();

        // A built set sends the options' model, so each request shows which options its client read.
        var set = QuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out var urgent).Build().Value;
        var typesafe = provider.GetRequiredKeyedService<IDecisionClient>("typesafe");
        var openRouter = provider.GetRequiredKeyedService<IDecisionClient>("openrouter");
        var typesafeResult = await typesafe.EvaluateAsync(set, "Help!");
        var openRouterResult = await openRouter.EvaluateAsync(set, "Help!");

        Assert.NotSame(typesafe, openRouter);
        Assert.Null(provider.GetService<IDecisionClient>());
        Assert.Equal(0.95, typesafeResult.Value.Get(urgent).Probability, 3);
        Assert.True(openRouterResult.IsSuccess);

        var typesafeRequest = OnlyRequest(typesafeHandler);
        Assert.Equal(new Uri("http://typesafe.local/v1/systemone"), typesafeRequest.Uri);
        Assert.Equal("Bearer typesafe-key", typesafeRequest.Authorization);
        Assert.Contains("\"model\":\"jev-typesafe\"", typesafeRequest.Body, StringComparison.Ordinal);
        Assert.Matches(UserAgentPattern, typesafeRequest.UserAgent);

        var openRouterRequest = OnlyRequest(openRouterHandler);
        Assert.Equal(new Uri("http://openrouter.local/api/v1/systemone"), openRouterRequest.Uri);
        Assert.Equal("Bearer openrouter-key", openRouterRequest.Authorization);
        Assert.Contains("\"model\":\"jev-openrouter\"", openRouterRequest.Body, StringComparison.Ordinal);
        Assert.Matches(UserAgentPattern, openRouterRequest.UserAgent);

        // Each name's options hold their own provider, which the wire cannot show: the base addresses are overridden.
        var monitor = provider.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>();
        Assert.Equal(DecisionProvider.TypeSafe, monitor.Get("typesafe").Provider);
        Assert.Equal(DecisionProvider.OpenRouter, monitor.Get("openrouter").Provider);
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var typesafeHttp = factory.CreateClient("Minos.NET:typesafe");
        using var openRouterHttp = factory.CreateClient("Minos.NET:openrouter");
        Assert.Equal(TimeSpan.FromSeconds(7), typesafeHttp.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(9), openRouterHttp.Timeout);
    }

    [Fact]
    public void KeyedClient_HasAnInfiniteHandlerLifetime_AndAPooledPrimaryHandler()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("openrouter", Options("http://keyed.local/"));
        using var provider = services.BuildServiceProvider();

        var lifetime = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get("Minos.NET:openrouter").HandlerLifetime;
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("Minos.NET:openrouter"));

        Assert.Equal(Timeout.InfiniteTimeSpan, lifetime);
        var primary = Assert.IsType<SocketsHttpHandler>(chain[^1]);
        Assert.Equal(TimeSpan.FromMinutes(2), primary.PooledConnectionLifetime);
    }

    [Fact]
    public async Task RepeatKeyedCalls_StackTheirConfiguration_AndRegisterOneClient()
    {
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddDecisionClient("openrouter", Options("http://first.local/", apiKey: "first-key")).HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddDecisionClient("openrouter", options => options.ApiKey = "second-key");
        services.AddDecisionClient("openrouter");
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredKeyedService<IDecisionClient>("openrouter").GetService<DecisionClient>()!.EvaluateAsync(Request());

        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(IDecisionClient)).ToArray();
        Assert.True(registrations.Length == 1, $"Expected one IDecisionClient registration, found {registrations.Length}.");
        var request = OnlyRequest(handler);
        Assert.Equal(new Uri("http://first.local/v1/systemone"), request.Uri);
        Assert.Equal("Bearer second-key", request.Authorization);
        Assert.Matches(UserAgentPattern, request.UserAgent);
    }

    [Fact]
    public void NullOrEmptyArguments_Throw_AndRegisterNothing()
    {
        IServiceCollection? none = null;
        var services = new ServiceCollection();
        Action<DecisionClientOptions> configure = _ => { };

        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddDecisionClient("openrouter")).ParamName);
        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddDecisionClient("openrouter", configure)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient((string)null!)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient(null!, configure)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => services.AddDecisionClient(string.Empty)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => services.AddDecisionClient(string.Empty, configure)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient("openrouter", (Action<DecisionClientOptions>)null!)).ParamName);
        Assert.Empty(services);
    }
}
