namespace Minos;

/// <summary>Starts a <see cref="DecisionClientBuilder"/> from a client.</summary>
public static class DecisionClientBuilderExtensions
{
    /// <summary>Creates a builder whose stages wrap <paramref name="innerClient"/>.</summary>
    /// <param name="innerClient">The client to wrap.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder AsBuilder(this IDecisionClient innerClient) => new(innerClient);
}
