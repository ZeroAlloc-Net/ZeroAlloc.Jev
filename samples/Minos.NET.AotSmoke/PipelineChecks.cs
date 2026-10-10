using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// The decision client pipeline under Native AOT: the neutral response's answer list, the client metadata, a custom
/// <see cref="DelegatingDecisionClient"/>, <see cref="DecisionClientBuilder"/> with its three <c>Use</c> overloads, the
/// three standard stages built by hand and through their <c>Use…</c> extensions, and
/// <see cref="DecisionClientOptions.UseStandardPipeline"/>.
/// </summary>
internal static class PipelineChecks
{
    [Covers("Minos.QuestionAnswerList.GetEnumerator() -> Minos.QuestionAnswerList.Enumerator")]
    [Covers("Minos.QuestionAnswerList.Enumerator.MoveNext() -> bool")]
    [Covers("Minos.QuestionAnswerList.QuestionAnswerList() -> void")]
    [Covers("Minos.QuestionAnswerList.Enumerator.Enumerator() -> void")]
    [Covers("Minos.QuestionAnswer.QuestionAnswer() -> void")]
    public static void ResponseAnswersEnumerateInDefinitionOrder()
    {
        var response = TriageResponse();
        var keys = new List<string>();
        var kinds = new List<QuestionKind>();
        foreach (var answer in response.Answers)
        {
            keys.Add(answer.Key);
            kinds.Add(answer.Kind);
        }

        var empty = new QuestionAnswerList();
        var emptyEnumerator = new QuestionAnswerList.Enumerator();
        var blank = new QuestionAnswer();

        Program.Check(
            keys is ["requests_credentials", "team", "urgency"]
                && kinds is [QuestionKind.Noul, QuestionKind.Choice, QuestionKind.Score]
                && response.Answers[1].ChosenIndex == 1
                && response.Answers[2].Probabilities.Length == 3,
            "a DecisionResponse's answers enumerate in the definition's order with their keys and kinds under Native AOT");
        Program.Check(
            empty.Count == 0 && !emptyEnumerator.MoveNext() && blank.Value == 0 && blank.Probabilities.IsEmpty,
            "the default answer list, enumerator and answer are empty under Native AOT");
    }

    [Covers("Minos.DecisionClientMetadata.DecisionClientMetadata(string! ProviderName, System.Uri? Endpoint, string? DefaultModel) -> void")]
    public static void MetadataDescribesTheClient()
    {
        var metadata = new DecisionClientMetadata("smoke", new Uri("https://example.test/api/"), "jev-latest");
        using var http = Program.Http(HttpStatusCode.OK, Program.NoulResponse);
        using var client = new DecisionClient(http, Program.Options());
        var real = client.GetService<DecisionClientMetadata>();

        Program.Check(
            metadata is { ProviderName: "smoke", Endpoint.Host: "example.test", DefaultModel: "jev-latest" }
                && metadata == metadata with { }
                && real is { ProviderName: "typesafe", Endpoint.Host: "example.test", DefaultModel: "jev-latest" },
            "DecisionClientMetadata carries a provider, endpoint and default model, and a DecisionClient offers its own, under Native AOT");
    }

    [Covers("Minos.DelegatingDecisionClient.DelegatingDecisionClient(Minos.IDecisionClient! innerClient) -> void")]
    [Covers("virtual Minos.DelegatingDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("virtual Minos.DelegatingDecisionClient.GetService(System.Type! serviceType, object? serviceKey = null) -> object?")]
    [Covers("Minos.DelegatingDecisionClient.Dispose() -> void")]
    [Covers("virtual Minos.DelegatingDecisionClient.Dispose(bool disposing) -> void")]
    public static async Task CustomStageDelegatesEveryCall()
    {
        var inner = new FixedResponseClient(TriageResponse());
        var stage = new CountingStage(inner);
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);

        var result = await stage.EvaluateAsync(request).ConfigureAwait(false);

        // A stage with no overrides, so each GetService call binds to DelegatingDecisionClient's own virtual.
        var passThrough = new PassThroughStage(inner);
        var self = passThrough.GetService(typeof(PassThroughStage));
        var found = passThrough.GetService(typeof(FixedResponseClient));
        var keyed = passThrough.GetService(typeof(PassThroughStage), serviceKey: "other");
        stage.Dispose();

        Program.Check(
            result.IsSuccess && ReferenceEquals(result.Value, inner.Response) && stage.Calls == 1 && ReferenceEquals(inner.LastRequest, request),
            "a DelegatingDecisionClient subclass passes the request to its inner client and returns its response under Native AOT");
        Program.Check(
            ReferenceEquals(self, passThrough) && ReferenceEquals(found, inner) && keyed is null,
            "a DelegatingDecisionClient finds itself and asks its inner client for anything else under Native AOT");
        Program.Check(
            stage.DisposedWith is true && inner.Disposed,
            "DelegatingDecisionClient.Dispose runs Dispose(true), which disposes the inner client, under Native AOT");
    }

    [Covers("Minos.DecisionClientBuilder.DecisionClientBuilder(Minos.IDecisionClient! innerClient) -> void")]
    [Covers("Minos.DecisionClientBuilder.DecisionClientBuilder(System.Func<System.IServiceProvider!, Minos.IDecisionClient!>! innerClientFactory) -> void")]
    [Covers("Minos.DecisionClientBuilder.Use(System.Func<Minos.IDecisionClient!, Minos.IDecisionClient!>! stageFactory) -> Minos.DecisionClientBuilder!")]
    [Covers("Minos.DecisionClientBuilder.Use(System.Func<Minos.IDecisionClient!, System.IServiceProvider!, Minos.IDecisionClient!>! stageFactory) -> Minos.DecisionClientBuilder!")]
    [Covers("Minos.DecisionClientBuilder.Use(System.Func<Minos.DecisionRequest!, Minos.IDecisionClient!, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>>! evaluate) -> Minos.DecisionClientBuilder!")]
    [Covers("Minos.DecisionClientBuilder.Build(System.IServiceProvider? services = null) -> Minos.IDecisionClient!")]
    [Covers("static Minos.DecisionClientBuilderExtensions.AsBuilder(this Minos.IDecisionClient! innerClient) -> Minos.DecisionClientBuilder!")]
    public static async Task BuilderComposesStagesInTheOrderTheyAreAdded()
    {
        var inner = new FixedResponseClient(TriageResponse());
        var services = new ServiceCollection().BuildServiceProvider();
        IServiceProvider? seen = null;
        IDecisionClient? innermostNext = null;

        var pipeline = new DecisionClientBuilder(inner)
            .Use(next => new CountingStage(next))
            .Use((next, provider) =>
            {
                seen = provider;
                return next;
            })
            .Use((request, next, cancellationToken) =>
            {
                innermostNext = next;
                return next.EvaluateAsync(request, cancellationToken);
            })
            .Build(services);
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        var result = await pipeline.EvaluateAsync(request).ConfigureAwait(false);

        var fromFactory = new DecisionClientBuilder(provider => new FixedResponseClient(TriageResponse())).Build();
        var bare = inner.AsBuilder().Build();
        var factoryResult = await fromFactory.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            result.IsSuccess
                && pipeline is CountingStage { Calls: 1 }
                && ReferenceEquals(innermostNext, inner)
                && ReferenceEquals(seen, services)
                && ReferenceEquals(inner.LastRequest, request),
            "DecisionClientBuilder wraps the first stage added outermost and the last next to the inner client, runs a delegate stage and passes Build its services under Native AOT");
        Program.Check(
            factoryResult.IsSuccess && fromFactory is FixedResponseClient && ReferenceEquals(bare, inner),
            "DecisionClientBuilder builds from a factory, and AsBuilder with no stages returns the client itself, under Native AOT");
        pipeline.Dispose();
        await services.DisposeAsync().ConfigureAwait(false);
    }

    [Covers("Minos.DecisionRetryOptions.DecisionRetryOptions() -> void")]
    [Covers("static Minos.DecisionRetryOptions.IsTransient(Minos.DecisionError! error) -> bool")]
    [Covers("Minos.RetryingDecisionClient.RetryingDecisionClient(Minos.IDecisionClient! innerClient, Minos.DecisionRetryOptions? options = null, Microsoft.Extensions.Logging.ILogger? logger = null, System.TimeProvider? timeProvider = null) -> void")]
    [Covers("override Minos.RetryingDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.LoggingDecisionClient.LoggingDecisionClient(Minos.IDecisionClient! innerClient, Microsoft.Extensions.Logging.ILogger! logger) -> void")]
    [Covers("override Minos.LoggingDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.OpenTelemetryDecisionClient.OpenTelemetryDecisionClient(Minos.IDecisionClient! innerClient) -> void")]
    [Covers("override Minos.OpenTelemetryDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    public static async Task StandardStagesBuiltByHandTraceLogAndRetry()
    {
        using var spans = new SpanRecorder();
        using var provider = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([provider], new LoggerFilterOptions { MinLevel = LogLevel.Debug });
        var logger = factory.CreateLogger("Minos.DecisionClient");
        var handler = new SequenceHandler(Program.TriageResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        var options = new DecisionRetryOptions { MaxRetries = 1, InitialBackoff = TimeSpan.FromMilliseconds(10), Jitter = false };
        var bare = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key", UseStandardPipeline = false });
        var retrying = new RetryingDecisionClient(bare, options, logger, TimeProvider.System);
        var logging = new LoggingDecisionClient(retrying, logger);
        using var client = new OpenTelemetryDecisionClient(logging);
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);

        var result = await client.EvaluateAsync(request).ConfigureAwait(false);
        var records = provider.Records;
        var attempts = handler.Calls;
        var spanCount = spans.Count;

        // Each inner stage called on its own: the logging stage logs without a span, the retry stage does neither.
        var viaLogging = await logging.EvaluateAsync(request).ConfigureAwait(false);
        var viaRetrying = await retrying.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            result.IsSuccess && attempts == 2 && spanCount == 1,
            "OpenTelemetryDecisionClient, LoggingDecisionClient and RetryingDecisionClient built by hand trace one span over a retried call under Native AOT");
        Program.Check(
            records is [{ EventId: 1003 }, { EventId: 1001 }] && records[1].Field("Operation") is "evaluate-set",
            "the hand-built stages log the retry and then the success under Native AOT");
        Program.Check(
            viaLogging.IsSuccess && viaRetrying.IsSuccess && handler.Calls == 4 && spans.Count == 1 && provider.Records.Length == 3,
            "LoggingDecisionClient and RetryingDecisionClient called on their own pass the call through under Native AOT");
        Program.Check(
            DecisionRetryOptions.IsTransient(new DecisionError(DecisionErrorKind.Overloaded, "busy"))
                && !DecisionRetryOptions.IsTransient(new DecisionError(DecisionErrorKind.Validation, "bad"))
                && new DecisionRetryOptions() is { MaxRetries: 2, Jitter: true },
            "DecisionRetryOptions.IsTransient retries an overload but not a validation error, and the options keep their defaults, under Native AOT");
    }

    [Covers("static Minos.RetryingDecisionClientBuilderExtensions.UseRetries(this Minos.DecisionClientBuilder! builder, System.Action<Minos.DecisionRetryOptions!>? configure = null) -> Minos.DecisionClientBuilder!")]
    [Covers("static Minos.LoggingDecisionClientBuilderExtensions.UseLogging(this Minos.DecisionClientBuilder! builder, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null) -> Minos.DecisionClientBuilder!")]
    [Covers("static Minos.OpenTelemetryDecisionClientBuilderExtensions.UseOpenTelemetry(this Minos.DecisionClientBuilder! builder) -> Minos.DecisionClientBuilder!")]
    public static async Task StandardStagesAddedThroughTheBuilder()
    {
        using var spans = new SpanRecorder();
        using var provider = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([provider], new LoggerFilterOptions { MinLevel = LogLevel.Debug });
        var handler = new SequenceHandler(Program.TriageResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        var bare = new DecisionClient(
            http, new DecisionClientOptions { ApiKey = "smoke-key", UseStandardPipeline = false, MaxRetries = 3, InitialBackoff = TimeSpan.FromMilliseconds(10) });
        var services = new ServiceCollection().AddSingleton<ILoggerFactory>(factory).BuildServiceProvider();
        var configured = 0;

        // UseLogging and UseRetries take the logger factory from the services Build is given.
        using var client = bare.AsBuilder()
            .UseOpenTelemetry()
            .UseLogging()
            .UseRetries(retry =>
            {
                configured = retry.MaxRetries;
                retry.Jitter = false;
            })
            .Build(services);
        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);

        Program.Check(
            SmokeAnswers.IsTriage(result) && handler.Calls == 2 && spans.Count == 1 && provider.Records is [{ EventId: 1003 }, { EventId: 1001 }],
            "UseOpenTelemetry, UseLogging and UseRetries add the standard stages around a bare client, logging through the services' factory, under Native AOT");
        Program.Check(
            client is OpenTelemetryDecisionClient
                && client.GetService<LoggingDecisionClient>() is not null
                && client.GetService<RetryingDecisionClient>() is not null
                && configured == 3,
            "the builder's stages are found through GetService, and UseRetries starts from the client's retry settings, under Native AOT");
        await services.DisposeAsync().ConfigureAwait(false);
    }

    public static async Task StandardPipelineCanBeTurnedOff()
    {
        var standardHandler = new SequenceHandler(Program.TriageResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var standardHttp = new HttpClient(standardHandler) { BaseAddress = new Uri("https://example.test/api/") };
        using var standard = new DecisionClient(
            standardHttp, new DecisionClientOptions { ApiKey = "smoke-key", InitialBackoff = TimeSpan.FromMilliseconds(10), Jitter = false });
        var bareHandler = new SequenceHandler(Program.TriageResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var bareHttp = new HttpClient(bareHandler) { BaseAddress = new Uri("https://example.test/api/") };
        using var bare = new DecisionClient(bareHttp, new DecisionClientOptions { ApiKey = "smoke-key", UseStandardPipeline = false });

        var retried = await standard.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);
        var single = await bare.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);

        Program.Check(
            SmokeAnswers.IsTriage(retried)
                && standardHandler.Calls == 2
                && standard.GetService<RetryingDecisionClient>() is not null
                && standard.GetService<OpenTelemetryDecisionClient>() is not null,
            "the standard pipeline retries an overloaded call and offers its stages under Native AOT");
        Program.Check(
            single.IsFailure
                && single.Error.Kind == DecisionErrorKind.Overloaded
                && bareHandler.Calls == 1
                && bare.GetService<RetryingDecisionClient>() is null
                && bare.GetService<OpenTelemetryDecisionClient>() is null,
            "UseStandardPipeline = false sends one attempt with no stages under Native AOT");
    }

    // The triage answers Program.TriageResponse carries, built with the public constructor as a fake would.
    internal static DecisionResponse TriageResponse()
        => new(
            SmokeTriage.Definition,
            [QuestionAnswer.Noul(0.1), QuestionAnswer.Choice(1, 0.7, [0.2, 0.8]), QuestionAnswer.Score(2, 1.9, 0.8, [0.0, 0.1, 0.9])],
            "jev-1.13.0");

    /// <summary>A stage that adds nothing: every member is <see cref="DelegatingDecisionClient"/>'s own. The
    /// pass-through allocation gate measures it.</summary>
    internal sealed class PassThroughStage(IDecisionClient innerClient) : DelegatingDecisionClient(innerClient);

    /// <summary>A stage that counts its calls and records how it was disposed, delegating both to its base.</summary>
    private sealed class CountingStage(IDecisionClient innerClient) : DelegatingDecisionClient(innerClient)
    {
        public int Calls { get; private set; }

        public bool? DisposedWith { get; private set; }

        public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return base.EvaluateAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            DisposedWith = disposing;
            base.Dispose(disposing);
        }
    }

    /// <summary>Records the Minos spans that stop while it listens.</summary>
    private sealed class SpanRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        private int _count;

        public SpanRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = static source => source.Name is "Minos",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _ => Interlocked.Increment(ref _count),
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public int Count => Volatile.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
