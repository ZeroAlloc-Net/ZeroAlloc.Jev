using Minos.Telemetry;
using ZeroAlloc.Results;

namespace Minos.Tests;

/// <summary>
/// An <see cref="IDecisionOperations"/> that answers every call from canned values, so the generated proxy's spans and
/// metrics can be checked without a transport.
/// </summary>
internal sealed class FakeOperations : IDecisionOperations
{
    /// <summary>Gets or sets the error every call fails with; <see langword="null"/> succeeds.</summary>
    public DecisionError? Error { get; set; }

    /// <summary>Gets or sets a delay each call waits first, so its duration has a known minimum; zero waits none.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Gets or sets the exception every call throws, after it yields; <see langword="null"/> throws none.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Gets or sets the raw path's response.</summary>
    public SystemOneResponse? Raw { get; set; }

    /// <summary>Gets or sets the model list.</summary>
    public ModelList Models { get; set; } = new() { Models = [] };

    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
        SystemOneRequest request, string provider, Uri endpoint, CancellationToken ct)
        => Complete(Error is { } error
            ? Result<SystemOneResponse, DecisionError>.Failure(error)
            : Result<SystemOneResponse, DecisionError>.Success(Raw ?? throw new InvalidOperationException("Set Raw first.")));

    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string provider, Uri endpoint, CancellationToken ct)
        => Complete(Error is { } error ? Result<ModelList, DecisionError>.Failure(error) : Result<ModelList, DecisionError>.Success(Models));

    private ValueTask<TResult> Complete<TResult>(TResult result)
        => Throws is not null || Delay > TimeSpan.Zero ? LaterAsync(result) : new ValueTask<TResult>(result);

    private async ValueTask<TResult> LaterAsync<TResult>(TResult result)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay);
        }
        else
        {
            await Task.Yield();
        }

        if (Throws is { } thrown)
        {
            throw thrown;
        }

        return result;
    }
}
