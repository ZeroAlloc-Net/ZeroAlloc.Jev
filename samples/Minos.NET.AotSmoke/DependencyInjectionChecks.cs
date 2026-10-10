using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Minos.AotSmoke;

/// <summary><c>AddDecisionClient</c> under Native AOT: default and keyed clients, configured by delegates or bound from configuration, registered, resolved and evaluated.</summary>
internal static class DependencyInjectionChecks
{
    /// <summary>Registers the default client over a canned handler. The allocation gates register theirs the same way.</summary>
    public static void RegisterDefaultClient(IServiceCollection services)
        => services
            .AddDecisionClient(options =>
            {
                options.ApiKey = "smoke-key";
                options.BaseAddress = new Uri("https://example.test/api/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));

    [Covers("static Microsoft.Extensions.DependencyInjection.DecisionServiceCollectionExtensions.AddDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, System.Action<Minos.DecisionClientOptions!>! configure) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.DecisionServiceCollectionExtensions.AddDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name, System.Action<Minos.DecisionClientOptions!>! configure) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    public static async Task DefaultAndKeyedClientsEvaluate()
    {
        var services = new ServiceCollection();
        RegisterDefaultClient(services);
        services
            .AddDecisionClient("openrouter", options =>
            {
                options.Provider = DecisionProvider.OpenRouter;
                options.ApiKey = "smoke-openrouter-key";
                options.BaseAddress = new Uri("https://openrouter.example.test/api/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!;
        var keyed = provider.GetRequiredKeyedService<IDecisionClient>("openrouter").GetService<DecisionClient>()!;
        var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);
        var keyedResult = await keyed.EvaluateAsync(Program.Request()).ConfigureAwait(false);

        Program.Check(
            ReferenceEquals(client, provider.GetRequiredService<IDecisionClient>()) && !ReferenceEquals(client, keyed),
            "AddDecisionClient registers one default client and a separate keyed client under Native AOT");
        Program.Check(
            result.IsSuccess && keyedResult.IsSuccess,
            "the default and the keyed client each evaluate through the factory's HttpClient under Native AOT");
    }

    /// <summary>The smoke app's configuration: a default client and an OpenRouter client, each in its own section.</summary>
    public static IConfigurationRoot BoundConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Minos:ApiKey"] = "smoke-key",
                ["Minos:BaseAddress"] = "https://example.test/api/",
                ["Minos:Timeout"] = "00:00:30",
                ["Minos:MaxRetries"] = "3",
                ["Minos:InitialBackoff"] = "00:00:00.250",
                ["Minos:MaxRetryDelay"] = "00:00:10",
                ["Minos:Jitter"] = "false",
                ["OpenRouter:Provider"] = "OpenRouter",
                ["OpenRouter:ApiKey"] = "smoke-openrouter-key",
                ["OpenRouter:BaseAddress"] = "https://openrouter.example.test/api/",
                ["OpenRouter:Model"] = "jev-1.13.0",
            })
            .Build();

    /// <summary>Registers the default client bound from <see cref="BoundConfiguration"/>, over a canned handler.</summary>
    public static void RegisterBoundDefaultClient(IServiceCollection services)
        => services
            .AddDecisionClient(BoundConfiguration().GetSection("Minos"))
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));

    [Covers("static Microsoft.Extensions.DependencyInjection.DecisionServiceCollectionExtensions.AddDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, Microsoft.Extensions.Configuration.IConfiguration! configuration) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.DecisionServiceCollectionExtensions.AddDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name, Microsoft.Extensions.Configuration.IConfiguration! configuration) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    public static async Task ClientsBoundFromConfigurationEvaluate()
    {
        var services = new ServiceCollection();
        RegisterBoundDefaultClient(services);
        services
            .AddDecisionClient("openrouter", BoundConfiguration().GetSection("OpenRouter"))
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>();
        var defaults = monitor.Get(Options.DefaultName);
        var keyed = monitor.Get("openrouter");

        Program.Check(
            defaults is { ApiKey: "smoke-key", MaxRetries: 3, Jitter: false }
                && defaults.Timeout == TimeSpan.FromSeconds(30)
                && defaults.InitialBackoff == TimeSpan.FromMilliseconds(250)
                && defaults.MaxRetryDelay == TimeSpan.FromSeconds(10)
                && defaults.BaseAddress == new Uri("https://example.test/api/"),
            "AddDecisionClient binds every default client setting from configuration under Native AOT");
        Program.Check(
            keyed is { Provider: DecisionProvider.OpenRouter, ApiKey: "smoke-openrouter-key", Model: "jev-1.13.0" },
            "AddDecisionClient binds a keyed client's own section under Native AOT");

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Program.Request()).ConfigureAwait(false);
        var keyedResult = await provider.GetRequiredKeyedService<IDecisionClient>("openrouter").GetService<DecisionClient>()!
            .EvaluateAsync(Program.Request()).ConfigureAwait(false);
        Program.Check(
            result.IsSuccess && keyedResult.IsSuccess,
            "the default and the keyed client bound from configuration each evaluate under Native AOT");
    }

    [Covers("static Microsoft.Extensions.DependencyInjection.DecisionServiceCollectionExtensions.AddDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.DecisionServiceCollectionExtensions.AddDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    public static async Task ClientsConfiguredFromTheEnvironmentEvaluate()
    {
        using (SmokeAssert.TypeSafeEnvironment())
        {
            var services = new ServiceCollection();
            services.AddDecisionClient().ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
            services.AddDecisionClient("secondary").ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
            using var provider = services.BuildServiceProvider();

            var client = provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!;
            var keyed = provider.GetRequiredKeyedService<IDecisionClient>("secondary").GetService<DecisionClient>()!;
            var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);
            var keyedResult = await keyed.EvaluateAsync(Program.Request()).ConfigureAwait(false);

            Program.Check(
                !ReferenceEquals(client, keyed) && result.IsSuccess && keyedResult.IsSuccess,
                "AddDecisionClient() and AddDecisionClient(name) take the key and base address from the environment and evaluate under Native AOT");
        }

        var unconfigured = new ServiceCollection();
        unconfigured.AddDecisionClient();
        using var unconfiguredProvider = unconfigured.BuildServiceProvider();
        using (SmokeAssert.NoApiKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<OptionsValidationException>(() => unconfiguredProvider.GetRequiredService<IDecisionClient>()),
                "AddDecisionClient() with no key in options or the environment fails options validation under Native AOT");
        }
    }

    public static void InvalidConfigurationFailsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = "smoke-key",
                ["BaseAddress"] = "https://example.test/api/",
                ["MaxRetries"] = "11",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddDecisionClient(configuration);
        using var provider = services.BuildServiceProvider();

        string? failure = null;
        try
        {
            _ = provider.GetRequiredService<IDecisionClient>();
        }
        catch (OptionsValidationException exception)
        {
            failure = exception.Message;
        }

        Program.Check(
            string.Equals(failure, "MaxRetries must be between 0 and 10. (Parameter 'options')", StringComparison.Ordinal),
            "an invalid bound value fails options validation with the core's message under Native AOT");
    }
}
