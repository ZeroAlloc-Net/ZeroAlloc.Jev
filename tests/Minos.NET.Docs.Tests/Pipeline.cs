namespace Minos.Docs.Tests;

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#region Pipeline_Default
using Minos;

// One yes/no question, so the examples stay short.
[Questions]
public partial record PipelineCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

public static class DefaultPipeline
{
    // A DecisionClient already runs the standard pipeline: one span and one set of metrics per call, one log record when
    // it was given a logger factory, and up to two retries of a transient failure. There is nothing to set up.
    public static async Task<bool> IsUrgentAsync(
        HttpClient http, string apiKey, string message, CancellationToken cancellationToken)
    {
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = apiKey });

        // The typed call builds a DecisionRequest from PipelineCheck.Definition and the message, and sends it through
        // the pipeline.
        var result = await client.EvaluateAsync<PipelineCheck>(message, cancellationToken);
        return result.IsSuccess && result.Value.IsUrgent.Value;
    }
}
#endregion


public static class CustomPipeline
{
    #region Pipeline_Custom
    // The standard stages, in the standard order, with four retries and an audit stage of your own.
    public static IDecisionClient Build(HttpClient http, string apiKey, ILoggerFactory loggerFactory, IAuditLog log)
    {
        // With the standard pipeline off, the client is the bare transport: one attempt per call, and no span or log.
        var transport = new DecisionClient(http, new DecisionClientOptions { ApiKey = apiKey, UseStandardPipeline = false });

        // The first Use is the outermost stage. The audit stage is the innermost, so it records every attempt.
        return transport.AsBuilder()
            .UseOpenTelemetry()
            .UseLogging(loggerFactory)
            .UseRetries(options => options.MaxRetries = 4)
            .Use(inner => new AuditStage(inner, log))
            .Build();
    }
    #endregion

    #region Pipeline_Delegate
    // A one-off stage from a delegate: it times each call and passes the result on unchanged.
    public static IDecisionClient Timed(IDecisionClient client, Action<TimeSpan> record)
        => client.AsBuilder()
            .Use(async (request, inner, cancellationToken) =>
            {
                var started = Stopwatch.GetTimestamp();
                var result = await inner.EvaluateAsync(request, cancellationToken);
                record(Stopwatch.GetElapsedTime(started));
                return result;
            })
            .Build();
    #endregion

    #region Pipeline_GetService
    public static async Task<string> DescribeAsync(IDecisionClient client, CancellationToken cancellationToken)
    {
        // The provider, the endpoint and the default model, which the DecisionClient at the bottom provides.
        var metadata = client.GetService<DecisionClientMetadata>();

        // A stage, found by its type: null when the pipeline has none, as with UseStandardPipeline off.
        var retries = client.GetService<RetryingDecisionClient>();

        // The raw System One calls are on the concrete DecisionClient, not on IDecisionClient.
        var models = await client.GetService<DecisionClient>()!.ListModelsAsync(cancellationToken);

        var listed = models.IsSuccess ? $"{models.Value.Models.Count} models" : models.Error.Kind.ToString();
        return $"{metadata?.ProviderName} at {metadata?.Endpoint}, retries {(retries is null ? "off" : "on")}, {listed}";
    }
    #endregion
}

public static class PipelineRegistration
{
    #region Pipeline_DI
    public static void AddAuditedDecision(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<TraceHeaderHandler>();
        var decision = services.AddDecisionClient(configuration.GetSection("Minos"));

        // Handlers go on the HttpClient builder, and see every attempt, retries included.
        decision.HttpClient.AddHttpMessageHandler<TraceHeaderHandler>();

        // Stages wrap the registered client, outside its standard pipeline, and can read the container's services.
        decision.Use((inner, provider) => new AuditStage(inner, provider.GetRequiredService<IAuditLog>()));
    }
    #endregion
}
