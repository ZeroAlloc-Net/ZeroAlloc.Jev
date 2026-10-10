using Minos.Protocols;
using ZeroAlloc.Rest;

namespace Minos.Transport;

/// <summary>Maps every ZeroAlloc.Rest failure to a <see cref="DecisionError"/>.</summary>
/// <param name="time">The clock used to turn an HTTP-date <c>Retry-After</c> into a delay.</param>
/// <param name="disposed">
/// Reads whether the <see cref="DecisionClient"/> that owns the <see cref="HttpClient"/> has been disposed, or
/// <see langword="null"/> for a borrowed <see cref="HttpClient"/>, which disposing the client never tears down.
/// </param>
/// <param name="protocol">The wire format that maps an error status and its body.</param>
internal sealed class DecisionErrorMapper(TimeProvider time, Func<bool>? disposed, IDecisionProtocol protocol) : IHttpErrorMapper<DecisionError>
{
    private const string DisposedMessage = "The client was disposed while the request was in flight.";

    /// <summary>The failure for an attempt that the client's disposal tore down or kept from being sent.</summary>
    /// <param name="exception">The exception the teardown caused, or <see langword="null"/> when nothing was sent.</param>
    /// <returns>A <see cref="DecisionErrorKind.Disposed"/> error.</returns>
    public static DecisionError Disposed(Exception? exception)
        => new(DecisionErrorKind.Disposed, DisposedMessage) { Exception = exception };

    public DecisionError Map(HttpError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // Disposing the client disposes its HttpClient, which cancels every request in flight: the attempt arrives here
        // as a time-out, or as a transport failure from a handler that fails that way. The client sets its flag before
        // it disposes the HttpClient, so the flag is visible here; a real time-out mapped before the disposal stays
        // Timeout. Deciding here, before the retry proxy sees the failure, keeps a disposed client from retrying.
        if (error.Kind is HttpErrorKind.Timeout or HttpErrorKind.Transport && disposed is not null && disposed())
        {
            return Disposed(error.Exception);
        }

        return error.Kind switch
        {
            HttpErrorKind.Status => protocol.MapError((int)error.StatusCode, error.Body.Span, error.BodyTruncated, error.ContentType, RetryAfter(error)),
            HttpErrorKind.Timeout => new DecisionError(DecisionErrorKind.Timeout, "The request timed out.") { Exception = error.Exception },
            HttpErrorKind.Transport => new DecisionError(
                DecisionErrorKind.Network, error.Message ?? "The request could not be sent.") { Exception = error.Exception },
            HttpErrorKind.Deserialization => new DecisionError(
                DecisionErrorKind.InvalidResponse,
                error.Message ?? "The response could not be read.")
            {
                StatusCode = (int)error.StatusCode,
                Exception = error.Exception,
            },
            // Defensive: a future ZeroAlloc.Rest release may add a kind this mapper does not know about yet.
            _ => new DecisionError(
                DecisionErrorKind.InvalidResponse,
                "Unrecognized error kind " + error.Kind.ToString() + ".")
            {
                StatusCode = (int)error.StatusCode,
                Exception = error.Exception,
            },
        };
    }

    private TimeSpan? RetryAfter(HttpError error)
    {
        string? retryAfter = null;
        foreach (var header in error.Headers)
        {
            if (header.Value.Count == 0)
            {
                continue;
            }

            // retry-after-ms (used by TypeSafe's official SDKs) is more precise, so it wins when it is valid.
            if (string.Equals(header.Key, "retry-after-ms", StringComparison.OrdinalIgnoreCase)
                && RetryAfterHeader.ParseMilliseconds(header.Value[0]) is { } milliseconds)
            {
                return milliseconds;
            }

            if (string.Equals(header.Key, "Retry-After", StringComparison.OrdinalIgnoreCase))
            {
                retryAfter = header.Value[0];
            }
        }

        return retryAfter is null ? null : RetryAfterHeader.Parse(retryAfter, time.GetUtcNow());
    }
}
