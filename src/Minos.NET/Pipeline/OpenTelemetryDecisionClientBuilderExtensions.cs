namespace Minos;

/// <summary>Adds <see cref="OpenTelemetryDecisionClient"/> to a pipeline.</summary>
public static class OpenTelemetryDecisionClientBuilderExtensions
{
    /// <summary>Adds a telemetry stage.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseOpenTelemetry(this DecisionClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(inner => new OpenTelemetryDecisionClient(inner));
    }
}
