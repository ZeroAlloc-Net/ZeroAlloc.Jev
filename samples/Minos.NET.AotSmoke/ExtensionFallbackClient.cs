using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// A hand-written <see cref="IDecisionClient"/> that is not a <see cref="DecisionClient"/>, as a test fake is, so every
/// typed and built-set call on it runs through <see cref="DecisionClientExtensions"/>. Proves that path compiles and
/// runs under Native AOT, not just over a real <see cref="DecisionClient"/>. It answers every call with the same
/// <see cref="DecisionResponse"/>, and records the request each extension builds, so a check can assert the state
/// reached it.
/// </summary>
internal sealed class ExtensionFallbackClient(DecisionResponse response) : IDecisionClient
{
    /// <summary>The request the last call passed to <see cref="EvaluateAsync"/>, or <see langword="null"/>.</summary>
    public DecisionRequest? LastRequest { get; private set; }

    /// <summary>The token the last call passed to <see cref="EvaluateAsync"/>.</summary>
    public CancellationToken LastToken { get; private set; }

    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        LastToken = cancellationToken;
        return new(Result<DecisionResponse, DecisionError>.Success(response));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
