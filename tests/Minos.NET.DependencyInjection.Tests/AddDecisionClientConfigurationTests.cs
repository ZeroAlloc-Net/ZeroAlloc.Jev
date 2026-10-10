using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary><c>AddDecisionClient</c> with options bound from <see cref="IConfiguration"/>.</summary>
public sealed class AddDecisionClientConfigurationTests
{
    [Fact]
    public void EveryKey_Binds()
    {
        var configuration = Configuration(
            ("Minos:Provider", "OpenRouter"),
            ("Minos:ApiKey", "bound-key"),
            ("Minos:BaseAddress", "http://bound.local/api/"),
            ("Minos:Model", "jev-1.13.0"),
            ("Minos:Timeout", "00:00:05"),
            ("Minos:MaxRetries", "4"),
            ("Minos:InitialBackoff", "00:00:00.100"),
            ("Minos:MaxRetryDelay", "00:00:03"),
            ("Minos:Jitter", "false"));
        var services = new ServiceCollection();
        services.AddDecisionClient(configuration.GetSection("Minos"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Equal(DecisionProvider.OpenRouter, options.Provider);
        Assert.Equal("bound-key", options.ApiKey);
        Assert.Equal(new Uri("http://bound.local/api/"), options.BaseAddress);
        Assert.Equal("jev-1.13.0", options.Model);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Timeout);
        Assert.Equal(4, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(3), options.MaxRetryDelay);
        Assert.False(options.Jitter);
    }

    [Fact]
    public async Task BoundClient_SendsToTheBoundBaseAddress()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/api/"), ("MaxRetries", "0"));
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddDecisionClient(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(new Uri("http://bound.local/api/v1/systemone"), OnlyRequest(handler).Uri);
    }

    [Fact]
    public void KeyedAndDefaultBindings_StaySeparate()
    {
        var configuration = Configuration(
            ("Default:ApiKey", "default-key"),
            ("Default:BaseAddress", "http://default.local/"),
            ("Default:MaxRetries", "1"),
            ("OpenRouter:Provider", "OpenRouter"),
            ("OpenRouter:ApiKey", "openrouter-key"),
            ("OpenRouter:MaxRetries", "5"));
        var services = new ServiceCollection();
        services.AddDecisionClient(configuration.GetSection("Default"));
        services.AddDecisionClient("openrouter", configuration.GetSection("OpenRouter"));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>();

        var defaults = monitor.Get(Microsoft.Extensions.Options.Options.DefaultName);
        var keyed = monitor.Get("openrouter");

        Assert.Equal((DecisionProvider.TypeSafe, "default-key", 1), (defaults.Provider, defaults.ApiKey, defaults.MaxRetries));
        Assert.Equal((DecisionProvider.OpenRouter, "openrouter-key", 5), (keyed.Provider, keyed.ApiKey, keyed.MaxRetries));
        Assert.NotSame(provider.GetRequiredService<IDecisionClient>(), provider.GetRequiredKeyedService<IDecisionClient>("openrouter"));
    }

    [Fact]
    public void ConfigureAfterBinding_OverridesTheBoundValue()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("MaxRetries", "4"));
        var services = new ServiceCollection();
        services.AddDecisionClient(configuration);
        services.AddDecisionClient(options => options.MaxRetries = 0);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Equal(0, options.MaxRetries);
        Assert.Equal("bound-key", options.ApiKey);
    }

    [Fact]
    public void InvalidBoundValue_FailsValidation_WithTheCoresMessage()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"), ("MaxRetries", "11"));
        var services = new ServiceCollection();
        services.AddDecisionClient("bound", configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("bound"));

        Assert.Equal(["MaxRetries must be between 0 and 10. (Parameter 'options')"], exception.Failures);
    }

    [Fact]
    public void UnconvertibleBoundValue_FailsAtTheFirstResolve()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"), ("MaxRetries", "abc"));
        var services = new ServiceCollection();
        services.AddDecisionClient(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Contains("MaxRetries", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArguments_Throw_AtRegistration()
    {
        var services = new ServiceCollection();
        var configuration = Configuration();
        IServiceCollection? none = null;

        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddDecisionClient(configuration)).ParamName);
        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddDecisionClient("bound", configuration)).ParamName);
        Assert.Equal("configuration", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient((IConfiguration)null!)).ParamName);
        Assert.Equal("configuration", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient("bound", (IConfiguration)null!)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient(null!, configuration)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => services.AddDecisionClient(string.Empty, configuration)).ParamName);
    }

    private static IConfigurationRoot Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
