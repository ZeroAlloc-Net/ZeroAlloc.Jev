namespace Minos.Transport;

/// <summary>
/// <see cref="DecisionClientOptions"/> resolved against the environment. A class rather than a record so the API key never
/// appears in a generated <c>ToString</c>.
/// </summary>
internal sealed class DecisionClientSettings
{
    private DecisionClientSettings(
        DecisionProvider provider,
        string apiKey,
        Uri baseAddress,
        string model,
        TimeSpan timeout,
        int maxRetries,
        TimeSpan initialBackoff,
        TimeSpan maxRetryDelay,
        bool jitter)
    {
        Provider = provider;
        ApiKey = apiKey;
        BaseAddress = baseAddress;
        Model = model;
        Timeout = timeout;
        MaxRetries = maxRetries;
        InitialBackoff = initialBackoff;
        MaxRetryDelay = maxRetryDelay;
        Jitter = jitter;
    }

    // The longest span HttpClient.Timeout accepts, and the cap on the back-off options.
    internal static readonly TimeSpan MaxMilliseconds = TimeSpan.FromMilliseconds(int.MaxValue);

    public DecisionProvider Provider { get; }

    public string ApiKey { get; }

    public Uri BaseAddress { get; }

    public string Model { get; }

    public TimeSpan Timeout { get; }

    public int MaxRetries { get; }

    public TimeSpan InitialBackoff { get; }

    public TimeSpan MaxRetryDelay { get; }

    public bool Jitter { get; }

    /// <summary>Resolves options: explicit values first, then environment variables, then provider defaults.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public static DecisionClientSettings Resolve(DecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new DecisionClientOptions();

        EnsureKnownProvider(options);
        EnsureValidTimeout(options);

        RetrySettings.Check(options.MaxRetries, options.InitialBackoff, options.MaxRetryDelay, nameof(options));

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ArgumentException("The model must not be blank.", nameof(options));
        }

        var openRouter = options.Provider == DecisionProvider.OpenRouter;
        var apiKey = ResolveApiKey(options, openRouter, environment);
        var baseAddress = ResolveBaseAddress(options, openRouter, environment);

        return new DecisionClientSettings(
            options.Provider,
            apiKey,
            WithTrailingSlash(baseAddress),
            options.Model.Trim(),
            options.Timeout,
            options.MaxRetries,
            options.InitialBackoff,
            options.MaxRetryDelay,
            options.Jitter);
    }

    /// <summary>
    /// Resolves only what an <see cref="HttpClient"/> needs: the base address and the per-attempt time-out. Runs the
    /// same checks as <see cref="Resolve"/> on those, with the same messages, and needs no API key.
    /// </summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">The provider, the time-out or the base address option is invalid.</exception>
    /// <exception cref="InvalidOperationException">The base address environment variable is invalid.</exception>
    public static DecisionHttpSettings ResolveHttp(DecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new DecisionClientOptions();

        EnsureKnownProvider(options);
        EnsureValidTimeout(options);

        var baseAddress = ResolveBaseAddress(options, options.Provider == DecisionProvider.OpenRouter, environment);
        return new DecisionHttpSettings(WithTrailingSlash(baseAddress), options.Timeout);
    }

    public override string ToString()
        => "Provider=" + Provider.ToString() + ", BaseAddress=" + BaseAddress.AbsoluteUri + ", ApiKey=***";

    private static void EnsureKnownProvider(DecisionClientOptions options)
    {
        if (!Enum.IsDefined(options.Provider))
        {
            throw new ArgumentException("Unknown provider " + options.Provider.ToString() + ".", nameof(options));
        }
    }

    private static void EnsureValidTimeout(DecisionClientOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero && options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException("The time-out must be positive.", nameof(options));
        }

        if (options.Timeout > MaxMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Timeout, "The time-out must not exceed " + MaxMilliseconds.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " milliseconds.");
        }
    }

    private static string ResolveApiKey(DecisionClientOptions options, bool openRouter, Func<string, string?> environment)
    {
        var keyVariable = openRouter ? DecisionDefaults.OpenRouterApiKeyEnvironmentVariable : DecisionDefaults.ApiKeyEnvironmentVariable;

        if (NonBlank(options.ApiKey) is { } explicitKey)
        {
            var trimmed = explicitKey.Trim();
            if (HasControlCharacter(trimmed))
            {
                throw new ArgumentException("The API key contains a control character.", nameof(options));
            }

            return trimmed;
        }

        if (NonBlank(environment(keyVariable)) is { } environmentKey)
        {
            var trimmed = environmentKey.Trim();
            if (HasControlCharacter(trimmed))
            {
                throw new InvalidOperationException("The " + keyVariable + " environment variable contains a control character.");
            }

            return trimmed;
        }

        throw new InvalidOperationException(
            "No API key is configured. Set DecisionClientOptions.ApiKey or the " + keyVariable + " environment variable.");
    }

    private static Uri ResolveBaseAddress(DecisionClientOptions options, bool openRouter, Func<string, string?> environment)
    {
        if (options.BaseAddress is { } explicitAddress)
        {
            if (!explicitAddress.IsAbsoluteUri)
            {
                throw new ArgumentException("The base address must be an absolute URI.", nameof(options));
            }

            if (HasQueryOrFragment(explicitAddress))
            {
                throw new ArgumentException("The base address must not contain a query or fragment.", nameof(options));
            }

            if (!IsHttpScheme(explicitAddress))
            {
                throw new ArgumentException("The base address must use the http or https scheme.", nameof(options));
            }

            return explicitAddress;
        }

        // TYPESAFE_BASE_URL applies only to DecisionProvider.TypeSafe: OpenRouter's base address is never taken from it, so
        // an OpenRouter key is never sent to a TypeSafe proxy.
        if (!openRouter && FromEnvironment(environment(DecisionDefaults.BaseAddressEnvironmentVariable)) is { } environmentAddress)
        {
            return environmentAddress;
        }

        return openRouter ? DecisionDefaults.OpenRouterBaseAddress : DecisionDefaults.TypeSafeBaseAddress;
    }

    private static bool HasControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Uri? FromEnvironment(string? value)
    {
        if (NonBlank(value) is not { } text)
        {
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "The " + DecisionDefaults.BaseAddressEnvironmentVariable + " environment variable is not an absolute URI.");
        }

        if (HasQueryOrFragment(uri))
        {
            throw new InvalidOperationException(
                "The " + DecisionDefaults.BaseAddressEnvironmentVariable + " environment variable must not contain a query or fragment.");
        }

        if (!IsHttpScheme(uri))
        {
            throw new InvalidOperationException(
                "The " + DecisionDefaults.BaseAddressEnvironmentVariable + " environment variable must use the http or https scheme.");
        }

        return uri;
    }

    private static bool HasQueryOrFragment(Uri uri) => uri.Query.Length > 0 || uri.Fragment.Length > 0;

    private static bool IsHttpScheme(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

    private static Uri WithTrailingSlash(Uri uri)
        => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}

/// <summary>The rules for retry settings, shared by <see cref="DecisionClientSettings"/> and <see cref="DecisionRetryOptions"/>.</summary>
internal static class RetrySettings
{
    /// <summary>Checks the retry count and the back-off span.</summary>
    /// <param name="maxRetries">The number of retries.</param>
    /// <param name="initialBackoff">The first back-off.</param>
    /// <param name="maxRetryDelay">The longest wait before a retry.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <exception cref="ArgumentException">A value is out of range.</exception>
    public static void Check(int maxRetries, TimeSpan initialBackoff, TimeSpan maxRetryDelay, string paramName)
    {
        if (maxRetries is < 0 or > 10)
        {
            throw new ArgumentException("MaxRetries must be between 0 and 10.", paramName);
        }

        if (initialBackoff <= TimeSpan.Zero || initialBackoff > DecisionClientSettings.MaxMilliseconds)
        {
            throw new ArgumentException("InitialBackoff must be positive and at most int.MaxValue milliseconds.", paramName);
        }

        if (maxRetryDelay < initialBackoff || maxRetryDelay > DecisionClientSettings.MaxMilliseconds)
        {
            throw new ArgumentException(
                "MaxRetryDelay must be at least InitialBackoff and at most int.MaxValue milliseconds.", paramName);
        }
    }

    /// <summary>Checks the failure predicate is set.</summary>
    /// <param name="shouldRetry">The predicate.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shouldRetry"/> is <see langword="null"/>.</exception>
    public static void CheckShouldRetry(object? shouldRetry, string paramName)
        => ArgumentNullException.ThrowIfNull(shouldRetry, paramName);
}
