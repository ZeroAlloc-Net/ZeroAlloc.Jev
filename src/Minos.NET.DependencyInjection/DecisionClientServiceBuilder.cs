using Microsoft.Extensions.DependencyInjection;

namespace Minos;

/// <summary>The pipeline builder of one <c>AddDecisionClient</c> registration, with the builder of its <see cref="System.Net.Http.HttpClient"/>.</summary>
/// <remarks>
/// Stages added here wrap the registered <see cref="DecisionClient"/>, which runs its own standard pipeline unless
/// <see cref="DecisionClientOptions.UseStandardPipeline"/> is <see langword="false"/>. The container builds the pipeline
/// once, when the client is first resolved, so stages read its services: <c>UseLogging()</c> its
/// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/> and <c>UseRetries()</c> its <see cref="TimeProvider"/>.
/// </remarks>
public sealed class DecisionClientServiceBuilder : DecisionClientBuilder
{
    internal DecisionClientServiceBuilder(IHttpClientBuilder httpClient, string? name, Func<IServiceProvider, IDecisionClient> innerClientFactory)
        : base(innerClientFactory)
    {
        HttpClient = httpClient;
        Name = name;
    }

    /// <summary>Gets the builder of the registration's named <see cref="System.Net.Http.HttpClient"/>, for adding handlers.</summary>
    public IHttpClientBuilder HttpClient { get; }

    /// <summary>Gets the registration's name, or <see langword="null"/> for the default registration.</summary>
    public string? Name { get; }
}
