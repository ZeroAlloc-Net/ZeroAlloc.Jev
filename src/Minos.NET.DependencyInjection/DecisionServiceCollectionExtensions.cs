using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minos;
using Minos.DependencyInjection;
using OptionsDefaults = Microsoft.Extensions.Options.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IDecisionClient"/> in an <see cref="IServiceCollection"/>, over <see cref="IHttpClientFactory"/>, with options from delegates or bound from <see cref="IConfiguration"/>.</summary>
/// <remarks>
/// Each registration returns a <see cref="DecisionClientServiceBuilder"/>, and stages added to it, also through repeat
/// calls, land on one pipeline around the registered client, built once when the container first resolves it.
/// Each registration is one <see cref="DecisionClient"/> singleton over its own named <see cref="HttpClient"/>, which
/// <see cref="DecisionClient.ConfigureHttpClient(HttpClient, DecisionClientOptions)"/> configures from the registration's named
/// <see cref="DecisionClientOptions"/>. The client reads those options once, when the container first builds it, and logs
/// through the container's <see cref="ILoggerFactory"/>. Its spans and metrics come from the <c>Minos</c> source
/// and meter. Invalid options fail a generic host at startup, through <see cref="DecisionClientOptions.Validate()"/> and
/// <c>ValidateOnStart</c>. Without a host they throw when the client is first resolved. Either way a value that fails
/// validation throws an <see cref="OptionsValidationException"/> that carries the core's message, and a configuration
/// value the binder cannot convert fails at the same point with the binder's <see cref="InvalidOperationException"/>.
/// The registration's named options are validated whenever they are first read, so creating its named
/// <see cref="HttpClient"/> from <see cref="IHttpClientFactory"/> directly also needs valid options, an API key
/// included. The options are validated even when the app registers its own <see cref="IDecisionClient"/> first, so a test
/// host that replaces the client still needs an API key, such as a placeholder one.
/// </remarks>
public static class DecisionServiceCollectionExtensions
{
    // The default client's HttpClient name; a keyed client's is this, a colon and its key.
    private const string HttpClientName = "Minos.NET";

    /// <summary>
    /// Registers the default <see cref="IDecisionClient"/>, configured from the default named <see cref="DecisionClientOptions"/>,
    /// defaults and environment variables.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <returns>The registration's pipeline builder; its <see cref="DecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static DecisionClientServiceBuilder AddDecisionClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddDefault(services, configure: null, configuration: null);
    }

    /// <summary>Registers the default <see cref="IDecisionClient"/>, configured by <paramref name="configure"/>.</summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">
    /// Configures the default named <see cref="DecisionClientOptions"/>. Each call adds its delegate, and they run in order.
    /// </param>
    /// <returns>The registration's pipeline builder; its <see cref="DecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static DecisionClientServiceBuilder AddDecisionClient(this IServiceCollection services, Action<DecisionClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        return AddDefault(services, configure, configuration: null);
    }

    /// <summary>
    /// Registers the default <see cref="IDecisionClient"/>, its <see cref="DecisionClientOptions"/> bound from
    /// <paramref name="configuration"/>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configuration">
    /// The configuration to bind, typically a section such as <c>builder.Configuration.GetSection("Minos")</c>. Its keys are
    /// the <see cref="DecisionClientOptions"/> property names. A configure delegate registered later for the default client
    /// overrides bound values. Changes after the client is built are not picked up.
    /// </param>
    /// <returns>The registration's pipeline builder; its <see cref="DecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    public static DecisionClientServiceBuilder AddDecisionClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return AddDefault(services, configure: null, configuration);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, configured from the
    /// <see cref="DecisionClientOptions"/> named <paramref name="name"/>, defaults and environment variables. Inject it with
    /// <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <returns>The registration's pipeline builder; its <see cref="DecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static DecisionClientServiceBuilder AddDecisionClient(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return AddKeyed(services, name, configure: null, configuration: null);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, configured by <paramref name="configure"/>.
    /// Inject it with <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <param name="configure">
    /// Configures the <see cref="DecisionClientOptions"/> named <paramref name="name"/>. Each call adds its delegate, and they
    /// run in order.
    /// </param>
    /// <returns>The registration's pipeline builder; its <see cref="DecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static DecisionClientServiceBuilder AddDecisionClient(this IServiceCollection services, string name, Action<DecisionClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddKeyed(services, name, configure, configuration: null);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, its <see cref="DecisionClientOptions"/> bound
    /// from <paramref name="configuration"/>. Inject it with <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <param name="configuration">
    /// The configuration to bind, typically a section such as <c>builder.Configuration.GetSection("Minos:OpenRouter")</c>.
    /// Its keys are the <see cref="DecisionClientOptions"/> property names. A configure delegate registered later for the same
    /// name overrides bound values. Changes after the client is built are not picked up.
    /// </param>
    /// <returns>The registration's pipeline builder; its <see cref="DecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static DecisionClientServiceBuilder AddDecisionClient(this IServiceCollection services, string name, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configuration);
        return AddKeyed(services, name, configure: null, configuration);
    }

    private static DecisionClientServiceBuilder AddDefault(
        IServiceCollection services, Action<DecisionClientOptions>? configure, IConfiguration? configuration)
    {
        var builder = AddOptionsAndHttpClient(services, OptionsDefaults.DefaultName, HttpClientName, name: null, configure, configuration);
        services.TryAddSingleton<IDecisionClient>(provider => builder.Build(provider));
        return builder;
    }

    private static DecisionClientServiceBuilder AddKeyed(
        IServiceCollection services, string name, Action<DecisionClientOptions>? configure, IConfiguration? configuration)
    {
        var httpClientName = HttpClientName + ":" + name;
        var builder = AddOptionsAndHttpClient(services, name, httpClientName, name, configure, configuration);
        services.TryAddKeyedSingleton<IDecisionClient>(name, (provider, _) => builder.Build(provider));
        return builder;
    }

    // Adds the options and configures the HttpClient. The HttpClient name maps one-to-one to the options name, so the
    // first registration of either is the first of both. That first registration alone also:
    // - creates the registration's pipeline builder, which a repeat call returns so stages land on one pipeline.
    // - adds the options' validator and ValidateOnStart, so repeat calls do not stack validators. TryAddEnumerable
    //   cannot do this: it compares implementation types, so it would keep one validator for every name.
    // - configures the HttpClient, so a repeat call cannot replace a primary handler the caller set through the first
    //   call's builder. The factory's own request logging is removed: its handlers allocate on every request even when
    //   nothing logs, and the client logs each operation and each retried attempt itself. AddDefaultLogger on the
    //   builder's HttpClient brings it back.
    private static DecisionClientServiceBuilder AddOptionsAndHttpClient(
        IServiceCollection services,
        string optionsName,
        string httpClientName,
        string? name,
        Action<DecisionClientOptions>? configure,
        IConfiguration? configuration)
    {
        var options = services.AddOptions<DecisionClientOptions>(optionsName);

        // Source-generated through EnableConfigurationBindingGenerator, so this Bind uses no reflection.
        if (configuration is not null)
        {
            options.Bind(configuration);
        }

        if (configure is not null)
        {
            options.Configure(configure);
        }

        var existing = FindBuilder(services, httpClientName);
        if (existing is not null)
        {
            return existing;
        }

        var httpClient = services.AddHttpClient(httpClientName);
        var builder = new DecisionClientServiceBuilder(httpClient, name, provider => Create(provider, optionsName, httpClientName));
        services.AddKeyedSingleton(httpClientName, builder);
        services.AddSingleton<IValidateOptions<DecisionClientOptions>>(new DecisionClientOptionsValidator(optionsName));
        options.ValidateOnStart();
        httpClient
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .ConfigureHttpClient((provider, client) => DecisionClient.ConfigureHttpClient(client, OptionsOf(provider, optionsName)))
            .RemoveAllLoggers();
        return builder;
    }

    // The builder AddDecisionClient already registered for the HttpClient httpClientName, or null. It looks for the keyed
    // instance only this class adds, so an IDecisionClient the app registered itself does not count.
    private static DecisionClientServiceBuilder? FindBuilder(IServiceCollection services, string httpClientName)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(DecisionClientServiceBuilder)
                && Equals(descriptor.ServiceKey, httpClientName))
            {
                return descriptor.KeyedImplementationInstance as DecisionClientServiceBuilder;
            }
        }

        return null;
    }

    private static DecisionClient Create(IServiceProvider provider, string optionsName, string httpClientName)
        => new(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName),
            OptionsOf(provider, optionsName),
            provider.GetService<ILoggerFactory>());

    private static DecisionClientOptions OptionsOf(IServiceProvider provider, string optionsName)
        => provider.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>().Get(optionsName);
}
