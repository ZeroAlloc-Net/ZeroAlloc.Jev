using Minos.Transport;

namespace Minos;

/// <summary>Configures a <see cref="DecisionClient"/>. Every property is optional.</summary>
/// <remarks>
/// A client reads these options once, when it is constructed. Changing this instance afterwards has no effect on a
/// client already built.
/// </remarks>
public sealed class DecisionClientOptions
{
    /// <summary>Gets or sets where requests go. Defaults to <see cref="DecisionProvider.TypeSafe"/>.</summary>
    public DecisionProvider Provider { get; set; } = DecisionProvider.TypeSafe;

    /// <summary>
    /// Gets or sets the API key. When unset, the client reads <see cref="DecisionDefaults.ApiKeyEnvironmentVariable"/> for
    /// TypeSafe or <see cref="DecisionDefaults.OpenRouterApiKeyEnvironmentVariable"/> for OpenRouter.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the API root, for a proxy or a test server. When unset, the client reads
    /// <see cref="DecisionDefaults.BaseAddressEnvironmentVariable"/> for <see cref="DecisionProvider.TypeSafe"/> only, then uses
    /// the provider's default address. <see cref="DecisionDefaults.BaseAddressEnvironmentVariable"/> never applies to
    /// <see cref="DecisionProvider.OpenRouter"/>, so an OpenRouter key is never sent to a TypeSafe proxy.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// Gets or sets the model that typed evaluation calls, including <c>EvaluateUtf8Async</c>, and built question sets
    /// ask, as a versioned id such as <c>jev-1.13.0</c> or an alias. Defaults to <see cref="DecisionDefaults.Model"/>.
    /// Surrounding whitespace is trimmed; a blank value is rejected.
    /// A <see cref="SystemOneRequest"/> names its own model, so this option does not apply to it.
    /// </summary>
    public string Model { get; set; } = DecisionDefaults.Model;

    /// <summary>
    /// Gets or sets how long a single attempt may take. Defaults to 60 seconds. Applies to an <see cref="HttpClient"/>
    /// the client creates, and to one configured with <see cref="DecisionClient.ConfigureHttpClient(HttpClient, DecisionClientOptions)"/>,
    /// as <c>AddDecisionClient</c>'s are; another <see cref="HttpClient"/> you pass keeps its own time-out. With retries, a call
    /// can take up to <c>(MaxRetries + 1) × Timeout</c> plus the waits between attempts. Must be positive or
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, and at most <see cref="int.MaxValue"/> milliseconds,
    /// which is about 24.8 days.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets how many times a failed call is retried after the first attempt. Defaults to 2; 0 turns retries off.
    /// </summary>
    /// <remarks>
    /// Retried: rate limiting (429), overload (503, 529), other server errors (5xx), request time-out (408), network
    /// failures and time-outs. Retrying a time-out, a network failure or a 5xx can process, and bill, a request twice when
    /// the first attempt reached the server; set 0 where that matters more than resilience. Valid range: 0–10.
    /// </remarks>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Gets or sets the first wait between attempts, doubled for each further retry and capped by
    /// <see cref="MaxRetryDelay"/>. Defaults to 500 ms. Must be positive and at most <see cref="int.MaxValue"/>
    /// milliseconds.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets the longest wait between attempts, for the backoff and for a server's <c>Retry-After</c> alike.
    /// Defaults to 30 seconds. Must be at least <see cref="InitialBackoff"/> and at most <see cref="int.MaxValue"/>
    /// milliseconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether a random jitter is added to the backoff, so many clients do not retry in
    /// step. Jitter adds up to 50 % of the backoff delay, and the result never exceeds <see cref="MaxRetryDelay"/>. A
    /// server's <c>Retry-After</c> is honoured exactly, up to <see cref="MaxRetryDelay"/>. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool Jitter { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the client runs its standard pipeline: telemetry, then logging when it has a logger factory,
    /// then retries. <see langword="false"/> leaves the bare transport, one attempt per call, to wrap with
    /// <see cref="DecisionClientBuilder"/>. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// It applies to the raw <see cref="DecisionClient.EvaluateAsync(SystemOneRequest, CancellationToken)"/> and
    /// <see cref="DecisionClient.ListModelsAsync(CancellationToken)"/> calls too: with <see langword="false"/> they send one
    /// attempt each and are not retried. They keep their own span and log either way.
    /// </remarks>
    public bool UseStandardPipeline { get; set; } = true;

    /// <summary>
    /// Checks these options exactly as a <see cref="DecisionClient"/> constructor does, including the API key and base address
    /// environment variables, and throws what the constructor would throw. It creates no client and changes no option.
    /// </summary>
    /// <remarks>
    /// The constructors run the same check, so options that pass here construct a client, as long as the environment
    /// variables do not change in between. <c>AddDecisionClient</c> runs it at host startup.
    /// </remarks>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">
    /// No API key is configured, or the <see cref="DecisionDefaults.ApiKeyEnvironmentVariable"/>,
    /// <see cref="DecisionDefaults.OpenRouterApiKeyEnvironmentVariable"/> or <see cref="DecisionDefaults.BaseAddressEnvironmentVariable"/>
    /// environment variable is invalid.
    /// </exception>
    public void Validate() => Validate(Environment.GetEnvironmentVariable);

    // The constructors' own check, against a given environment; tests pass a fake.
    internal void Validate(Func<string, string?> environment) => _ = DecisionClientSettings.Resolve(this, environment);
}
