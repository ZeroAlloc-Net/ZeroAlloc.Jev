using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// An inner client that answers every call with the same completed <see cref="DecisionResponse"/>, allocating nothing,
/// and records the last request and whether it was disposed.
/// </summary>
internal sealed class FixedResponseClient(DecisionResponse response) : IDecisionClient
{
    public DecisionResponse Response => response;

    public DecisionRequest? LastRequest { get; private set; }

    public bool Disposed { get; private set; }

    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return new(Result<DecisionResponse, DecisionError>.Success(response));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => Disposed = true;
}
