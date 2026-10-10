using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Minos;

/// <summary>
/// The client's log events, in the <see cref="Category"/> category. They carry metadata only: never the state,
/// instructions, criteria, answers, API key, a header value or <see cref="DecisionError.Detail"/>.
/// </summary>
/// <remarks>
/// No event has more than six fields, and each template keeps its placeholders in parameter order, so the generator
/// emits a <c>LoggerMessage.Define</c> call rather than a state struct, whose reference-type <c>GetEnumerator</c>
/// NetFabric.Hyperlinq.Analyzer's HLQ006 rejects.
/// </remarks>
internal static partial class DecisionLog
{
    /// <summary>The logger category: <c>loggerFactory.CreateLogger(Category)</c>.</summary>
    public const string Category = "Minos.DecisionClient";

    /// <summary>The operation name of <see cref="DecisionClient.EvaluateAsync(SystemOneRequest, CancellationToken)"/>.</summary>
    public const string Evaluate = "evaluate";

    /// <summary>The operation name of the typed <c>EvaluateAsync&lt;T&gt;</c> overloads.</summary>
    public const string EvaluateTyped = "evaluate-typed";

    /// <summary>The operation name of <see cref="DecisionClient.EvaluateAsync(QuestionSet, DecisionContent, CancellationToken)"/>.</summary>
    public const string EvaluateBuiltSet = "evaluate-built-set";

    /// <summary>The operation name of every neutral <see cref="IDecisionClient.EvaluateAsync"/> call, typed and built sets included.</summary>
    public const string EvaluateSet = "evaluate-set";

    /// <summary>The operation name of <see cref="DecisionClient.ListModelsAsync(CancellationToken)"/>.</summary>
    public const string ListModels = "list-models";

    /// <summary>
    /// What is logged instead of an <see cref="DecisionErrorKind.InvalidResponse"/> error's message, which can quote the
    /// response: an unknown option's value, or the characters a JSON reader rejected.
    /// </summary>
    public const string UnreadableResponse = "The response could not be read.";

    /// <summary>
    /// What is logged instead of a <see cref="DecisionErrorKind.Network"/> error's message, which is the transport's exception
    /// text and can echo the request: its URL or headers.
    /// </summary>
    public const string RequestNotSent = "The request could not be sent.";

    /// <summary>An evaluation succeeded.</summary>
    [LoggerMessage(
        EventId = 1001,
        EventName = nameof(EvaluationSucceeded),
        Level = LogLevel.Debug,
        Message = "Minos {Operation} on {Model} via {Provider} succeeded: {QuestionCount} questions in {DurationMs} ms.")]
    public static partial void EvaluationSucceeded(
        ILogger logger, string operation, string model, string provider, int questionCount, double durationMs);

    /// <summary>An evaluation failed, after any retries; the message is the error's <see cref="SafeMessage"/>.</summary>
    [LoggerMessage(
        EventId = 1002,
        EventName = nameof(EvaluationFailed),
        Level = LogLevel.Warning,
        Message = "Minos {Operation} on {Model} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}")]
    public static partial void EvaluationFailed(
        ILogger logger, string operation, string model, DecisionErrorKind errorKind, int? statusCode, double durationMs, string errorMessage);

    /// <summary>An attempt failed with an error the retry policy will retry.</summary>
    [LoggerMessage(
        EventId = 1003,
        EventName = nameof(AttemptRetrying),
        Level = LogLevel.Warning,
        Message = "Minos attempt {Attempt} failed with {ErrorKind}, status {StatusCode}, retry-after {RetryAfter}; retrying.")]
    public static partial void AttemptRetrying(ILogger logger, int attempt, DecisionErrorKind errorKind, int? statusCode, TimeSpan? retryAfter);

    /// <summary>A model listing succeeded.</summary>
    [LoggerMessage(
        EventId = 1004,
        EventName = nameof(ModelsListed),
        Level = LogLevel.Debug,
        Message = "Minos " + ListModels + " via {Provider} succeeded: {ModelCount} models in {DurationMs} ms.")]
    public static partial void ModelsListed(ILogger logger, DecisionProvider provider, int modelCount, double durationMs);

    /// <summary>A model listing failed, after any retries.</summary>
    [LoggerMessage(
        EventId = 1005,
        EventName = nameof(ModelsListFailed),
        Level = LogLevel.Warning,
        Message = "Minos " + ListModels + " via {Provider} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}")]
    public static partial void ModelsListFailed(
        ILogger logger, DecisionProvider provider, DecisionErrorKind errorKind, int? statusCode, double durationMs, string errorMessage);

    /// <summary>An operation threw: a programming error, since every call failure is returned as a <see cref="DecisionError"/>.</summary>
    [LoggerMessage(
        EventId = 1006,
        EventName = nameof(UnexpectedException),
        Level = LogLevel.Error,
        Message = "Minos {Operation} threw an unexpected exception.")]
    public static partial void UnexpectedException(ILogger logger, string operation, Exception exception);

    /// <summary>The message to log for <paramref name="error"/>: its own, except where it can carry request or response text.</summary>
    /// <param name="error">The error.</param>
    /// <returns><see cref="DecisionError.Message"/>, or a fixed text for <see cref="DecisionErrorKind.InvalidResponse"/> (<see cref="UnreadableResponse"/>) and <see cref="DecisionErrorKind.Network"/> (<see cref="RequestNotSent"/>).</returns>
    public static string SafeMessage(DecisionError error)
        => error.Kind switch
        {
            DecisionErrorKind.InvalidResponse => UnreadableResponse,
            DecisionErrorKind.Network => RequestNotSent,
            _ => error.Message,
        };

    /// <summary>
    /// Whether an operation could log anything: Debug for its success, Warning for its failure, Error for an unexpected
    /// exception. Checked per call, so a logger whose filter changes at run time is followed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <returns><see langword="true"/> when any of the three levels is enabled.</returns>
    public static bool IsAnyEnabled(ILogger logger)
        => logger.IsEnabled(LogLevel.Debug) || logger.IsEnabled(LogLevel.Warning) || logger.IsEnabled(LogLevel.Error);

    /// <summary>Takes a timestamp only when a success or failure event could be written.</summary>
    /// <param name="logger">The logger, or <see langword="null"/>.</param>
    /// <returns>A <see cref="Stopwatch"/> timestamp, or 0 when neither Debug nor Warning is enabled.</returns>
    public static long StartTiming(ILogger? logger)
        => logger is not null && (logger.IsEnabled(LogLevel.Debug) || logger.IsEnabled(LogLevel.Warning)) ? Stopwatch.GetTimestamp() : 0;

    /// <summary>The milliseconds since <paramref name="started"/>, or 0 when no timestamp was taken.</summary>
    /// <param name="started">A <see cref="StartTiming"/> result.</param>
    /// <returns>The elapsed milliseconds.</returns>
    public static double ElapsedMilliseconds(long started)
        => started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>
    /// An exception filter: logs <see cref="UnexpectedException"/> unless <paramref name="exception"/> is the caller's
    /// own cancellation, and returns <see langword="false"/>, so the exception propagates unchanged.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="operation">The operation name.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="ct">The caller's token.</param>
    /// <returns><see langword="false"/>.</returns>
    public static bool LogUnexpected(ILogger logger, string operation, Exception exception, CancellationToken ct)
    {
        if (!(exception is OperationCanceledException && ct.IsCancellationRequested))
        {
            UnexpectedException(logger, operation, exception);
        }

        return false;
    }
}
