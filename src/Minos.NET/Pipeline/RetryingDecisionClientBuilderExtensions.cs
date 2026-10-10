using Microsoft.Extensions.Logging;

namespace Minos;

/// <summary>Adds <see cref="RetryingDecisionClient"/> to a pipeline.</summary>
public static class RetryingDecisionClientBuilderExtensions
{
    /// <summary>Adds a retry stage.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Changes the settings. They start from the inner client's own retry settings when it offers them, such as a <see cref="DecisionClient"/>'s, and from the defaults otherwise.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>The stage logs through the build's <see cref="ILoggerFactory"/> and waits on its <see cref="TimeProvider"/>, when the services have them.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseRetries(this DecisionClientBuilder builder, Action<DecisionRetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use((inner, services) =>
        {
            var options = inner.GetService<DecisionRetryOptions>()?.Clone() ?? new DecisionRetryOptions();
            configure?.Invoke(options);
            var logger = (services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger(DecisionLog.Category);
            return new RetryingDecisionClient(inner, options, logger, services.GetService(typeof(TimeProvider)) as TimeProvider);
        });
    }
}
