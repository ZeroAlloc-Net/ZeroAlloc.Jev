using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using ZeroAlloc.Results;

namespace Minos.Docs.Tests;

// Runs each snippet of docs/pipeline.md over a scripted handler or a fake, so every example on the page is executed.
public sealed class PipelineTests
{
    private const string UrgentResponse = """
        {
          "model": "jev-1.13.0",
          "answers": { "is_urgent": { "type": "noul", "noul": 0.93 } },
          "usage": { "input_tokens": 41, "output_tokens": 3 }
        }
        """;

    private const string ModelsResponse = """
        {
          "models": [
            { "name": "jev-latest", "description": "The most recent stable release.", "release_date": "2026-09-15" },
            { "name": "jev-preview", "description": "The most recent release.", "release_date": "2026-09-16" }
          ]
        }
        """;

    [Fact]
    public async Task TheDefaultClient_RetriesATransientFailure()
    {
        var (http, requests) = ScriptedDecision.Http(new DecisionClientOptions(), Reply.Error(503), Reply.Ok(UrgentResponse));
        using (http)
        {
            Assert.True(await DefaultPipeline.IsUrgentAsync(http, "docs-key", "Help! The server is down.", CancellationToken.None));
        }

        Assert.Equal([null, "1"], requests.Select(sent => sent.RetryCount));
    }

    [Fact]
    public async Task TheCustomPipeline_TracesLogsRetriesAndAuditsEveryAttempt()
    {
        var log = new MemoryAuditLog();
        var (http, requests) = ScriptedDecision.Http(new DecisionClientOptions(), Reply.Error(503), Reply.Ok(UrgentResponse));
        using var provider = new FakeLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Debug));
        using (http)
        using (var client = CustomPipeline.Build(http, "docs-key", loggers, log))
        {
            Assert.IsType<OpenTelemetryDecisionClient>(client);
            Assert.NotNull(client.GetService<LoggingDecisionClient>());
            Assert.NotNull(client.GetService<AuditStage>());

            var result = await client.EvaluateAsync<PipelineCheck>("Help! The server is down.", CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        // The audit stage sits inside the retries, so it saw the failed attempt and the retry that succeeded.
        Assert.Equal([false, true], log.Outcomes);
        Assert.Equal([null, "1"], requests.Select(sent => sent.RetryCount));

        // The logging stage sits outside the retries, so the call is logged once, as a success.
        Assert.Equal([1001], provider.Collector.GetSnapshot().Select(record => record.Id.Id));
    }

    [Fact]
    public async Task TheDelegateStage_TimesEachCall_AndPassesTheResultOn()
    {
        var durations = new List<TimeSpan>();
        using var client = CustomPipeline.Timed(new AlwaysUrgent(), durations.Add);

        var result = await client.EvaluateAsync<PipelineCheck>("Help!", CancellationToken.None);

        Assert.True(result.Value.IsUrgent.Value);
        Assert.Collection(durations, _ => { });
    }

    [Fact]
    public async Task GetService_FindsTheMetadata_AStage_AndTheRawClient()
    {
        var (http, client, requests) = ScriptedDecision.Client(ScriptedDecision.Quick(), Reply.Ok(ModelsResponse));
        using (http)
        using (var wrapped = client.AsBuilder().Use(inner => new AuditStage(inner, new MemoryAuditLog())).Build())
        {
            var description = await CustomPipeline.DescribeAsync(wrapped, CancellationToken.None);

            Assert.Equal("typesafe at https://docs.example/api/, retries on, 2 models", description);
        }

        Assert.Collection(requests, _ => { });
    }

    [Fact]
    public async Task GetService_FindsNoRetryStage_WithTheStandardPipelineOff()
    {
        var options = ScriptedDecision.Quick();
        options.UseStandardPipeline = false;
        var (http, client, _) = ScriptedDecision.Client(options, Reply.Ok(ModelsResponse));
        using (http)
        using (client)
        {
            Assert.Equal(
                "typesafe at https://docs.example/api/, retries off, 2 models",
                await CustomPipeline.DescribeAsync(client, CancellationToken.None));
        }
    }

    [Fact]
    public async Task TheRegistration_AddsTheHandler_AndWrapsTheClientInTheAuditStage()
    {
        var log = new MemoryAuditLog();
        using var handler = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Minos:ApiKey"] = "docs-key" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IAuditLog>(log);
        PipelineRegistration.AddAuditedDecision(services, configuration);
        services.AddHttpClient("Minos.NET").ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();
        var result = await client.EvaluateAsync<PipelineCheck>("Help!", CancellationToken.None);

        Assert.True(result.Value.IsUrgent.Value);
        Assert.IsType<AuditStage>(client);
        Assert.NotNull(client.GetService<DecisionClient>());
        Assert.Equal([true], log.Outcomes);
        Assert.Collection(handler.Requests, sent => Assert.True(sent.HasTraceHeader));
    }

    [Fact]
    public async Task TheFake_AnswersAOneNoulSet()
    {
        using var client = new AlwaysUrgent();

        var result = await client.EvaluateAsync<PipelineCheck>("Any text.", CancellationToken.None);

        Assert.True(result.Value.IsUrgent.Value);
        Assert.Equal(0.9, result.Value.IsUrgent.Probability);
        Assert.Null(client.GetService<DecisionClientMetadata>());
    }

    private sealed class MemoryAuditLog : IAuditLog
    {
        private readonly List<bool> _outcomes = [];

        public IReadOnlyList<bool> Outcomes
        {
            get
            {
                lock (_outcomes)
                {
                    return [.. _outcomes];
                }
            }
        }

        public void Record(QuestionSetDefinition definition, Result<DecisionResponse, DecisionError> result)
        {
            Assert.Same(PipelineCheck.Definition, definition);
            lock (_outcomes)
            {
                _outcomes.Add(result.IsSuccess);
            }
        }
    }
}
