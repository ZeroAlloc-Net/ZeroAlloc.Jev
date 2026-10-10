using Microsoft.Extensions.Logging;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>A stage that retries failures <see cref="DecisionRetryOptions.ShouldRetry"/> accepts, with exponential backoff, jitter and a cap.</summary>
/// <remarks>
/// <para>
/// Each retry sends a copy of the request with <see cref="DecisionRequest.RetryAttempt"/> set. A response's
/// <see cref="DecisionError.RetryAfter"/> replaces the next backoff, within <see cref="DecisionRetryOptions.MaxRetryDelay"/>.
/// With a logger, each attempt that will be retried is logged as event 1003, AttemptRetrying.
/// </para>
/// <para>
/// After <see cref="DelegatingDecisionClient.Dispose()"/>, no retry starts: the call returns a
/// <see cref="DecisionErrorKind.Disposed"/> failure. A <see cref="DecisionErrorKind.Disposed"/> failure is never retried.
/// </para>
/// </remarks>
public sealed class RetryingDecisionClient : DelegatingDecisionClient
{
    private static readonly Func<(IDecisionClient Inner, DecisionRequest Request), int, CancellationToken, ValueTask<Result<DecisionResponse, DecisionError>>> Attempt
        = static (state, retry, ct) => state.Inner.EvaluateAsync(state.Request with { RetryAttempt = retry }, ct);

    private readonly DecisionRetry _retry;
    private volatile bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RetryingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client to retry.</param>
    /// <param name="options">The retry settings; <see langword="null"/> for the defaults.</param>
    /// <param name="logger">Logs each retried attempt; <see langword="null"/> for none.</param>
    /// <param name="timeProvider">Times the waits; <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is invalid.</exception>
    public RetryingDecisionClient(IDecisionClient innerClient, DecisionRetryOptions? options = null, ILogger? logger = null, TimeProvider? timeProvider = null)
        : base(innerClient)
        => _retry = new DecisionRetry(options ?? new DecisionRetryOptions(), logger, timeProvider ?? TimeProvider.System, () => _disposed);

    /// <inheritdoc />
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var first = InnerClient.EvaluateAsync(request, cancellationToken);

        // A completed success, or a failure that will not be retried, is returned as is: no state machine.
        if (first.IsCompletedSuccessfully)
        {
            var done = first.Result;
            return _retry.WillRetry(done, 0)
                ? _retry.ContinueAsync(done, (InnerClient, request), Attempt, cancellationToken)
                : new ValueTask<Result<DecisionResponse, DecisionError>>(done);
        }

        return _retry.RunAsync(first, (InnerClient, request), Attempt, cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
