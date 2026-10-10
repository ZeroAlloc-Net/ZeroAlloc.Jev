using Minos.Transport;

namespace Minos;

/// <summary>How <see cref="RetryingDecisionClient"/> retries.</summary>
public sealed class DecisionRetryOptions
{
    /// <summary>Gets or sets the number of retries after the first attempt. 0 turns retrying off.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Gets or sets the first retry's backoff; each later retry doubles it, up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets or sets the longest wait before a retry, including one a response asked for.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets whether each backoff is randomized, so many clients do not retry in step.</summary>
    public bool Jitter { get; set; } = true;

    /// <summary>Gets or sets which failures are retried. A <see cref="DecisionErrorKind.Disposed"/> failure never is.</summary>
    public Func<DecisionError, bool> ShouldRetry { get; set; } = IsTransient;

    /// <summary>Whether a failure is worth another attempt: rate limiting, overload, server errors, 408, network failures and time-outs.</summary>
    /// <param name="error">The failure.</param>
    /// <returns><see langword="true"/> when another attempt may succeed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static bool IsTransient(DecisionError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Kind is DecisionErrorKind.RateLimited or DecisionErrorKind.Overloaded or DecisionErrorKind.Server
                or DecisionErrorKind.Network or DecisionErrorKind.Timeout
            || error.StatusCode == 408;
    }

    internal DecisionRetryOptions Clone() => new()
    {
        MaxRetries = MaxRetries,
        InitialBackoff = InitialBackoff,
        MaxRetryDelay = MaxRetryDelay,
        Jitter = Jitter,
        ShouldRetry = ShouldRetry,
    };

    // The same rules and messages as DecisionClientOptions' retry settings: see RetrySettings.
    internal void Validate()
    {
        RetrySettings.CheckShouldRetry(ShouldRetry, nameof(ShouldRetry));
        RetrySettings.Check(MaxRetries, InitialBackoff, MaxRetryDelay, nameof(DecisionRetryOptions));
    }
}
