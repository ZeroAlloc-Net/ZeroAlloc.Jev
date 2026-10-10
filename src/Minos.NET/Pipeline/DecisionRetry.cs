using Microsoft.Extensions.Logging;
using Minos.Transport;
using ZeroAlloc.Resilience;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>
/// The retry loop shared by <see cref="RetryingDecisionClient"/> and <see cref="DecisionClient"/>'s raw calls. Delays come
/// from ZeroAlloc.Resilience's <see cref="RetryPolicy"/>, so backoff, jitter, the cap and a response's Retry-After behave
/// as they always have.
/// </summary>
internal sealed class DecisionRetry
{
    private readonly RetryPolicy _policy;
    private readonly Func<DecisionError, bool> _shouldRetry;
    private readonly ILogger? _logger;
    private readonly TimeProvider _time;
    private readonly Func<bool> _disposed;

    public DecisionRetry(DecisionRetryOptions options, ILogger? logger, TimeProvider time, Func<bool> disposed)
    {
        options.Validate();
        _policy = new RetryPolicy(
            maxAttempts: options.MaxRetries + 1,
            backoffMs: (int)Math.Ceiling(options.InitialBackoff.TotalMilliseconds),
            jitter: options.Jitter,
            perAttemptTimeoutMs: 0,
            maxDelayMs: (int)Math.Ceiling(options.MaxRetryDelay.TotalMilliseconds));
        _shouldRetry = options.ShouldRetry;
        _logger = logger;
        _time = time;
        _disposed = disposed;
    }

    /// <summary>Whether a result from the 0-based <paramref name="attempt"/> is followed by another attempt.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The attempt's result.</param>
    /// <param name="attempt">The 0-based attempt number.</param>
    /// <returns><see langword="true"/> when another attempt follows.</returns>
    public bool WillRetry<T>(in Result<T, DecisionError> result, int attempt)
        => result.IsFailure
            && attempt < _policy.MaxAttempts - 1
            && result.Error.Kind != DecisionErrorKind.Disposed
            && _shouldRetry(result.Error);

    /// <summary>Awaits <paramref name="first"/>, then retries while <see cref="WillRetry"/> says so.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="first">The first attempt's call, already started.</param>
    /// <param name="state">Passed to <paramref name="attempt"/>.</param>
    /// <param name="attempt">Starts the attempt with the given 1-based retry number.</param>
    /// <param name="ct">Cancels the waits; each attempt receives it too.</param>
    /// <returns>The last attempt's result.</returns>
    public async ValueTask<Result<T, DecisionError>> RunAsync<TState, T>(
        ValueTask<Result<T, DecisionError>> first,
        TState state,
        Func<TState, int, CancellationToken, ValueTask<Result<T, DecisionError>>> attempt,
        CancellationToken ct)
    {
        var result = await first.ConfigureAwait(false);
        for (var index = 0; WillRetry(result, index); index++)
        {
            var error = result.Error;
            if (_logger is { } logger && logger.IsEnabled(LogLevel.Warning))
            {
                DecisionLog.AttemptRetrying(logger, index + 1, error.Kind, error.StatusCode, error.RetryAfter);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_policy.GetDelayMs(index, error.RetryAfter)), _time, ct).ConfigureAwait(false);

            // A retry that would start after Dispose is not sent.
            if (_disposed())
            {
                return Result<T, DecisionError>.Failure(DecisionErrorMapper.Disposed(null));
            }

            result = await attempt(state, index + 1, ct).ConfigureAwait(false);
        }

        return result;
    }
}
