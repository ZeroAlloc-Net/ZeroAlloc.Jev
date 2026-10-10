using ZeroAlloc.Results;

namespace Minos;

/// <summary>Composes pipeline stages around an inner <see cref="IDecisionClient"/>.</summary>
/// <remarks>
/// The first <c>Use</c> is the outermost stage: a call passes through the stages in the order they were added, then
/// reaches the inner client. The <c>Use…</c> extension methods add the standard stages.
/// </remarks>
public class DecisionClientBuilder
{
    private readonly Func<IServiceProvider, IDecisionClient> _innerClientFactory;
    private readonly List<Func<IDecisionClient, IServiceProvider, IDecisionClient>> _stages = [];

    /// <summary>Initializes a new instance of the <see cref="DecisionClientBuilder"/> class around an existing client.</summary>
    /// <param name="innerClient">The client the stages wrap.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    public DecisionClientBuilder(IDecisionClient innerClient)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        _innerClientFactory = _ => innerClient;
    }

    /// <summary>Initializes a new instance of the <see cref="DecisionClientBuilder"/> class around a client created at <see cref="Build"/>.</summary>
    /// <param name="innerClientFactory">Creates the client the stages wrap from the build's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClientFactory"/> is <see langword="null"/>.</exception>
    public DecisionClientBuilder(Func<IServiceProvider, IDecisionClient> innerClientFactory)
    {
        ArgumentNullException.ThrowIfNull(innerClientFactory);
        _innerClientFactory = innerClientFactory;
    }

    /// <summary>Adds a stage.</summary>
    /// <param name="stageFactory">Wraps the client inside it.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stageFactory"/> is <see langword="null"/>.</exception>
    public DecisionClientBuilder Use(Func<IDecisionClient, IDecisionClient> stageFactory)
    {
        ArgumentNullException.ThrowIfNull(stageFactory);
        return Use((inner, _) => stageFactory(inner));
    }

    /// <summary>Adds a stage that needs the build's services.</summary>
    /// <param name="stageFactory">Wraps the client inside it, given the services passed to <see cref="Build"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stageFactory"/> is <see langword="null"/>.</exception>
    public DecisionClientBuilder Use(Func<IDecisionClient, IServiceProvider, IDecisionClient> stageFactory)
    {
        ArgumentNullException.ThrowIfNull(stageFactory);
        _stages.Add(stageFactory);
        return this;
    }

    /// <summary>Adds a stage from a delegate that runs around each call.</summary>
    /// <param name="evaluate">Receives the request, the client inside it and the cancellation token, and returns the result.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="evaluate"/> is <see langword="null"/>.</exception>
    public DecisionClientBuilder Use(Func<DecisionRequest, IDecisionClient, CancellationToken, ValueTask<Result<DecisionResponse, DecisionError>>> evaluate)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        return Use((inner, _) => new AnonymousDelegatingDecisionClient(inner, evaluate));
    }

    /// <summary>Creates the inner client and wraps it in the stages.</summary>
    /// <param name="services">The services stages and the inner client factory read; <see langword="null"/> for none.</param>
    /// <returns>The outermost client.</returns>
    /// <exception cref="InvalidOperationException">A factory returned <see langword="null"/>.</exception>
    public IDecisionClient Build(IServiceProvider? services = null)
    {
        services ??= EmptyServiceProvider.Instance;
        var client = _innerClientFactory(services)
            ?? throw new InvalidOperationException("The inner client factory returned null.");
        for (var i = _stages.Count - 1; i >= 0; i--)
        {
            client = _stages[i](client, services)
                ?? throw new InvalidOperationException($"The stage factory at position {i} returned null.");
        }

        return client;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
