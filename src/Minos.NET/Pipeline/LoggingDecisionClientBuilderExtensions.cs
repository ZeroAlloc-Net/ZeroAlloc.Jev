using Microsoft.Extensions.Logging;

namespace Minos;

/// <summary>Adds <see cref="LoggingDecisionClient"/> to a pipeline.</summary>
public static class LoggingDecisionClientBuilderExtensions
{
    /// <summary>Adds a logging stage, or nothing when there is no logger factory.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="loggerFactory">The factory; <see langword="null"/> reads one from the build's services.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseLogging(this DecisionClientBuilder builder, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use((inner, services) =>
        {
            var factory = loggerFactory ?? services.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
            return factory is null ? inner : new LoggingDecisionClient(inner, factory.CreateLogger(DecisionLog.Category));
        });
    }
}
