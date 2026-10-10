namespace Minos;

/// <summary>What a client says about where its calls go. <see cref="IDecisionClient.GetService"/> returns it; the telemetry stage reads it.</summary>
/// <param name="ProviderName">The provider's telemetry name, such as <c>typesafe</c> or <c>openrouter</c>.</param>
/// <param name="Endpoint">The base address calls go to, if there is one.</param>
/// <param name="DefaultModel">The model used when a request names none, if there is one.</param>
public sealed record DecisionClientMetadata(string ProviderName, Uri? Endpoint, string? DefaultModel);
