using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Minos.Serialization;

namespace Minos.Tests;

/// <summary>The standard pipeline <see cref="DecisionClient"/> composes, and <see cref="DecisionClientOptions.UseStandardPipeline"/>.</summary>
public sealed class DecisionClientPipelineTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    [Fact]
    public void Standard_pipeline_has_telemetry_and_retries_and_logging_only_with_a_factory()
    {
        using var plain = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"));
        Assert.NotNull(plain.GetService<OpenTelemetryDecisionClient>());
        Assert.NotNull(plain.GetService<RetryingDecisionClient>());
        Assert.Null(plain.GetService<LoggingDecisionClient>());

        using var logs = new LogCapture(LogLevel.Debug);
        using var logged = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"), loggerFactory: logs.Factory);
        Assert.NotNull(logged.GetService<LoggingDecisionClient>());
    }

    [Fact]
    public void Opt_out_leaves_the_bare_transport()
    {
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"), configure: o => o.UseStandardPipeline = false);

        Assert.Null(client.GetService<OpenTelemetryDecisionClient>());
        Assert.Null(client.GetService<RetryingDecisionClient>());
        Assert.NotNull(client.GetService<DecisionClientMetadata>());
    }

    [Fact]
    public async Task Opt_out_does_not_retry()
    {
        var handler = StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
        using var client = ClientTestKit.Client(_httpClients, handler, configure: o =>
        {
            o.UseStandardPipeline = false;
            o.MaxRetries = 2;
            o.InitialBackoff = TimeSpan.FromMilliseconds(1);
        });

        await client.EvaluateAsync<UrgencyCheck>("s");

        ClientTestKit.OnlyRequest(handler);
    }

    [Fact]
    public async Task Opt_out_does_not_retry_the_raw_calls()
    {
        var handler = StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
        using var client = ClientTestKit.Client(_httpClients, handler, configure: o =>
        {
            o.UseStandardPipeline = false;
            o.MaxRetries = 2;
            o.InitialBackoff = TimeSpan.FromMilliseconds(1);
        });

        await client.EvaluateAsync(JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!);
        await client.ListModelsAsync();

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Standard_pipeline_retries_with_the_options_settings()
    {
        var handler = StubHandler.Sequence(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture.Text("response-noul.json"), System.Text.Encoding.UTF8, "application/json") });
        using var client = ClientTestKit.Client(_httpClients, handler, configure: o =>
        {
            o.MaxRetries = 1;
            o.InitialBackoff = TimeSpan.FromMilliseconds(1);
        });

        var result = await client.EvaluateAsync<UrgencyCheck>("s");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("1", handler.Requests[1].RetryCount);
    }

    [Fact]
    public void Disposed_client_throws_at_entry()
    {
        var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"));
        client.Dispose();

        ClientTestKit.ThrowsSynchronously<ObjectDisposedException>(() => client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s")).AsTask());
        ClientTestKit.ThrowsSynchronously<ObjectDisposedException>(() => client.EvaluateAsync<UrgencyCheck>("s").AsTask());
    }

    [Fact]
    public void Retry_options_copy_reflects_the_settings()
    {
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"), configure: o => o.MaxRetries = 4);

        var first = client.GetService<DecisionRetryOptions>()!;
        first.MaxRetries = 9;

        Assert.Equal(4, client.GetService<DecisionRetryOptions>()!.MaxRetries);
    }

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }
}
