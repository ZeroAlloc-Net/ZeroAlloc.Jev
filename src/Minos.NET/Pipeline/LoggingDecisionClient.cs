using Microsoft.Extensions.Logging;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>A stage that logs each call's outcome once: event 1001 on success, 1002 on failure, 1006 for an unexpected exception.</summary>
/// <remarks>
/// It logs the provider and default model from the inner client's <see cref="DecisionClientMetadata"/>, and never the state,
/// the questions, the answers or an error response body. With every level it emits disabled, it returns the inner call
/// itself.
/// </remarks>
public sealed class LoggingDecisionClient : DelegatingDecisionClient
{
    private const string Unknown = "unknown";

    private readonly ILogger _logger;
    private readonly string _provider;
    private readonly string _defaultModel;

    /// <summary>Initializes a new instance of the <see cref="LoggingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client to log.</param>
    /// <param name="logger">The logger, typically for the <c>Minos.DecisionClient</c> category.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public LoggingDecisionClient(IDecisionClient innerClient, ILogger logger)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        var metadata = innerClient.GetService<DecisionClientMetadata>();
        _provider = metadata?.ProviderName ?? Unknown;
        _defaultModel = metadata?.DefaultModel ?? Unknown;
    }

    /// <inheritdoc />
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!DecisionLog.IsAnyEnabled(_logger))
        {
            return InnerClient.EvaluateAsync(request, cancellationToken);
        }

        var started = DecisionLog.StartTiming(_logger);
        ValueTask<Result<DecisionResponse, DecisionError>> call;
        try
        {
            call = InnerClient.EvaluateAsync(request, cancellationToken);
        }
        catch (Exception exception) when (DecisionLog.LogUnexpected(_logger, DecisionLog.EvaluateSet, exception, cancellationToken))
        {
            throw;
        }

        if (call.IsCompletedSuccessfully)
        {
            var result = call.Result;
            LogOutcome(request, result, started);
            return new(result);
        }

        return LogAsync(request, call, started, cancellationToken);
    }

    private async ValueTask<Result<DecisionResponse, DecisionError>> LogAsync(
        DecisionRequest request, ValueTask<Result<DecisionResponse, DecisionError>> call, long started, CancellationToken ct)
    {
        try
        {
            var result = await call.ConfigureAwait(false);
            LogOutcome(request, result, started);
            return result;
        }
        catch (Exception exception) when (DecisionLog.LogUnexpected(_logger, DecisionLog.EvaluateSet, exception, ct))
        {
            throw;
        }
    }

    private void LogOutcome(DecisionRequest request, in Result<DecisionResponse, DecisionError> result, long started)
    {
        var durationMs = DecisionLog.ElapsedMilliseconds(started);
        var model = request.Model ?? _defaultModel;
        if (result.IsSuccess)
        {
            DecisionLog.EvaluationSucceeded(_logger, DecisionLog.EvaluateSet, model, _provider, request.Definition.Questions.Count, durationMs);
        }
        else
        {
            var error = result.Error;
            DecisionLog.EvaluationFailed(_logger, DecisionLog.EvaluateSet, model, error.Kind, error.StatusCode, durationMs, DecisionLog.SafeMessage(error));
        }
    }
}
