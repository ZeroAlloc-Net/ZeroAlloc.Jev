using ZeroAlloc.Results;

namespace Minos;

/// <summary>A pipeline stage: an <see cref="IDecisionClient"/> that wraps another and forwards to it by default.</summary>
/// <remarks>
/// Override <see cref="EvaluateAsync"/> to act before or after the inner call. <see cref="GetService"/> returns this stage
/// when it is of the requested type, and asks the inner client otherwise. Disposing a stage disposes the inner client.
/// </remarks>
public abstract class DelegatingDecisionClient : IDecisionClient
{
    /// <summary>Initializes a new instance of the <see cref="DelegatingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client this stage wraps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    protected DelegatingDecisionClient(IDecisionClient innerClient)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        InnerClient = innerClient;
    }

    /// <summary>Gets the client this stage wraps.</summary>
    protected IDecisionClient InnerClient { get; }

    /// <inheritdoc />
    public virtual ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        => InnerClient.EvaluateAsync(request, cancellationToken);

    /// <inheritdoc />
    public virtual object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : InnerClient.GetService(serviceType, serviceKey);
    }

    /// <summary>Disposes this stage and the inner client.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes the inner client when <paramref name="disposing"/> is <see langword="true"/>.</summary>
    /// <param name="disposing">Whether the call comes from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            InnerClient.Dispose();
        }
    }
}
