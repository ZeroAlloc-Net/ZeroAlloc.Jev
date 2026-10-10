using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text;

namespace Minos.Docs.Tests;

/// <summary>Listeners are process-wide, so every test that attaches one runs alone, after the parallel ones.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DocsTelemetryListeners
{
    public const string Name = "Docs telemetry listeners";
}

[Collection(DocsTelemetryListeners.Name)]
public sealed class ObservabilityTelemetryTests
{
    private const string Page = "observability.md";
    private const string Model = "docs-observed-model";

    private const string TicketResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.92 },
            "department": { "type": "choice", "choice": "billing", "probabilities": { "billing": 0.7, "technical": 0.2, "sales": 0.1 }, "confidence": 0.64 },
            "mood": { "type": "score", "score": 0.5, "legend": { "0": "Annoyed or angry", "1": "Neutral", "2": "Pleased or grateful" }, "probabilities": { "0": 0.6, "1": 0.3, "2": 0.1 }, "confidence": 0.7 }
          },
          "usage": { "input_tokens": 150, "output_tokens": 20 }
        }
        """;

    private const string OpenRouterResponse = """
        {
          "id": "gen-1727400000-abc123",
          "provider": "TypeSafe",
          "model": "~typesafe/jev-latest",
          "answers": { "is_urgent": { "type": "noul", "noul": 0.95 } },
          "usage": { "input_tokens": 296, "output_tokens": 20, "cost": 0.000296 }
        }
        """;

    private const string UrgentResponse = """
        { "model": "jev-1.13.0", "answers": { "is_urgent": { "type": "noul", "noul": 0.93 } }, "usage": { "input_tokens": 41, "output_tokens": 3 } }
        """;

    private const string ModelsResponse = """
        { "models": [ { "name": "jev-latest", "description": "The most recent stable release.", "release_date": "2026-09-15" } ] }
        """;

    private static DecisionClientOptions Options(int maxRetries = 0)
    {
        var options = ScriptedDecision.Quick(maxRetries);
        options.Model = Model;
        return options;
    }

    private static async Task RunAsync(Func<DecisionClient, Task> call, DecisionClientOptions options, params Reply[] script)
    {
        var (http, client, _) = ScriptedDecision.Client(options, script);
        using (http)
        using (client)
        {
            await call(client);
        }
    }

    private static Task Typed(DecisionClient client) => client.EvaluateAsync<TicketAnalysis>("Help! SECRET-STATE", CancellationToken.None).AsTask();

    private static Task Raw(DecisionClient client) => RawRequests.UrgencyAsync(client, "Help! SECRET-STATE", CancellationToken.None);

    private static Task Listing(DecisionClient client) => client.ListModelsAsync(CancellationToken.None).AsTask();

    private static string[] Keys(IEnumerable<KeyValuePair<string, object?>> tags) => [.. tags.Select(tag => tag.Key)];

    private static string Value(KeyValuePair<string, object?>[] tags, string key)
        => Convert.ToString(tags.First(tag => string.Equals(tag.Key, key, StringComparison.Ordinal)).Value, CultureInfo.InvariantCulture)!;

    private static DecisionMeasurement[] Points(DecisionTelemetryListener listener, string metric)
        => [.. listener.Measurements.Where(point => string.Equals(point.Name, metric, StringComparison.Ordinal))];

    private static SortedSet<string> Attributes(DecisionTelemetryListener listener, string metric)
        => new(Points(listener, metric).SelectMany(point => Keys(point.Tags)), StringComparer.Ordinal);

    private static double[] Buckets(DecisionTelemetryListener listener, string metric)
        => [.. ((Histogram<double>)listener.Instruments[metric]).Advice!.HistogramBucketBoundaries!];

    private static string Describe(IEnumerable<KeyValuePair<string, object?>> tags)
        => string.Join(' ', tags.Select(tag => tag.Key + "=" + Convert.ToString(tag.Value, CultureInfo.InvariantCulture)));

    private static string Show(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static bool EveryStepIs(double[] boundaries, double factor)
        => boundaries.Zip(boundaries.Skip(1), (a, b) => Math.Abs((b / a) - factor) < 1e-9).All(step => step);

    [Fact]
    public async Task ATypedEvaluation_IsOneClientSpan_NamedAfterTheModel_WithTheTagsOnThePage()
    {
        using var listener = new DecisionTelemetryListener();

        await RunAsync(Typed, Options(), Reply.Ok(TicketResponse));

        var span = Only.Of(listener.Spans);
        Assert.Equal($"evaluate {Model}", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.Equal("evaluate", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("typesafe", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal(Model, span.GetTagItem("gen_ai.request.model"));
        Assert.Equal("docs.example", span.GetTagItem("server.address"));
        Assert.Equal(443, span.GetTagItem("server.port"));
        Assert.Equal("evaluate-set", span.GetTagItem("minos.operation"));
        Assert.Equal(3, span.GetTagItem("minos.request.question_count"));
        Assert.Equal("jev-1.13.0", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal(150, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(20, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Null(span.GetTagItem("gen_ai.response.id"));
    }

    [Fact]
    public async Task AModelListing_IsAListModelsSpan_ThatNamesNoModel()
    {
        using var listener = new DecisionTelemetryListener();

        await RunAsync(Listing, Options(), Reply.Ok(ModelsResponse));

        var span = Only.Of(listener.Spans);
        Assert.Equal("list_models", span.DisplayName);
        Assert.Equal("list_models", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("list-models", span.GetTagItem("minos.operation"));
        Assert.Null(span.GetTagItem("gen_ai.request.model"));
        Assert.Null(span.GetTagItem("minos.request.question_count"));
    }

    [Fact]
    public async Task OpenRouterNeverGetsAModelListingSpan_BecauseNoRequestIsMade()
    {
        using var listener = new DecisionTelemetryListener();
        var options = Options();
        options.Provider = DecisionProvider.OpenRouter;

        await RunAsync(Listing, options, Reply.Ok(ModelsResponse));

        Assert.Empty(listener.Spans);
    }

    [Fact]
    public async Task AFailedResult_MarksTheSpanError_WithItsKind_AndNoDescription()
    {
        using var listener = new DecisionTelemetryListener();

        await RunAsync(Typed, Options(), Reply.Error(422, "{\"detail\":\"SECRET-ERROR-BODY\"}"));

        var span = Only.Of(listener.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Null(span.StatusDescription);
        Assert.Equal("Validation", span.GetTagItem("error.type"));
        Assert.Null(span.GetTagItem("gen_ai.response.model"));
    }

    [Fact]
    public async Task ARetriedCall_IsOneMinosSpan_OverTheRestSpansOfItsAttempts()
    {
        var spans = new List<Activity>();
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Minos" or "ZeroAlloc.Rest",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span =>
            {
                lock (spans)
                {
                    spans.Add(span);
                }
            },
        };
        ActivitySource.AddActivityListener(activities);

        await RunAsync(Typed, Options(2), Reply.Error(503), Reply.Ok(TicketResponse));

        var clientSpan = Only.Of([.. spans.Where(span => span.Source.Name is "Minos")]);
        var rest = spans.Where(span => span.Source.Name is "ZeroAlloc.Rest").ToArray();
        Assert.Equal(2, rest.Length);
        Assert.All(rest, attempt => Assert.Equal(clientSpan.SpanId, attempt.ParentSpanId));
    }

    // The exceptions section of the page describes ZeroAlloc.Telemetry 1.11.0, whose exception path sets error.type.
    [Fact]
    public void TheTelemetryPin_IsStillTheVersionThePageDescribes()
    {
        var props = File.ReadAllText(Path.Combine(PublishedPages.Root, "Directory.Packages.props"));

        Assert.True(
            props.Contains("<PackageVersion Include=\"ZeroAlloc.Telemetry\" Version=\"1.11.0\" />", StringComparison.Ordinal),
            "ZeroAlloc.Telemetry is no longer pinned at 1.11.0. Check that the exception path still sets error.type "
            + "and leaves the message out, then update the 'When an exception is thrown' note in docs/observability.md and this test.");
    }

    [Fact]
    public async Task ACancelledCall_MarksTheSpanError_WithTheExceptionsTypeAndNoDescription()
    {
        using var listener = new DecisionTelemetryListener();
        var (http, client, _) = ScriptedDecision.Client(Options(), new Reply(HttpStatusCode.OK, TicketResponse, Delay: TimeSpan.FromSeconds(30)));
        OperationCanceledException raised;
        using (http)
        using (client)
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            raised = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await client.EvaluateAsync<TicketAnalysis>("Help!", cancel.Token));
        }

        var span = Only.Of(listener.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.True(string.IsNullOrEmpty(span.StatusDescription));
        Assert.Equal(raised.GetType().FullName, span.GetTagItem("error.type"));
        var duration = Only.Of(Points(listener, "gen_ai.client.operation.duration"));
        Assert.Equal(1, Keys(duration.Tags).Count(key => string.Equals(key, "error.type", StringComparison.Ordinal)));
        Assert.Equal(raised.GetType().FullName, duration.Tags.First(tag => string.Equals(tag.Key, "error.type", StringComparison.Ordinal)).Value);
    }

    [Fact]
    public void TheExceptionsSection_SaysWhatTheClientDoes()
    {
        var page = PageTables.Text(Page);
        var section = page[page.IndexOf("### When an exception is thrown", StringComparison.Ordinal)..];
        var end = section.IndexOf("\n## ", StringComparison.Ordinal);
        section = section[..end].ReplaceLineEndings(" ");

        Assert.Contains("`error.type`", section, StringComparison.Ordinal);
        Assert.Contains("full name", section, StringComparison.Ordinal);
        Assert.Contains("no description", section, StringComparison.Ordinal);
        Assert.DoesNotContain("1.10.0", section, StringComparison.Ordinal);
    }

    // The span table: every attribute on the page is one a span carries, and the start ones are the ones the sampler sees.
    [Fact]
    public async Task TheSpanTableOnThePage_IsTheAttributesTheClientSets()
    {
        using var listener = new DecisionTelemetryListener();
        await RunAsync(Typed, Options(), Reply.Ok(TicketResponse));
        await RunAsync(Raw, Options(), Reply.Ok(OpenRouterResponse));
        await RunAsync(Typed, Options(), Reply.Error(422));
        await RunAsync(Listing, Options(), Reply.Ok(ModelsResponse));

        var onSpans = new SortedSet<string>(listener.Spans.SelectMany(span => Keys(span.TagObjects)), StringComparer.Ordinal);
        var atStart = new SortedSet<string>(listener.StartTags.SelectMany(Keys), StringComparer.Ordinal);
        var rows = PageTables.Rows(Page, "The span's attributes");
        var all = new SortedSet<string>(rows.Select(row => PageTables.Code(row[0])), StringComparer.Ordinal);
        var start = new SortedSet<string>(
            rows.Where(row => row[1].StartsWith("start", StringComparison.Ordinal)).Select(row => PageTables.Code(row[0])),
            StringComparer.Ordinal);

        Assert.Equal(onSpans, all);
        Assert.Equal(atStart, start);
        Assert.Equal(
            ["evaluate", "evaluate-set", "list-models"],
            listener.Spans.Select(span => (string)span.GetTagItem("minos.operation")!).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal("gen-1727400000-abc123", listener.Spans[1].GetTagItem("gen_ai.response.id"));
        Assert.Equal(0.000296, listener.Spans[1].GetTagItem("minos.usage.cost"));
    }

    // The metric table: the meter has exactly these instruments, with these kinds and units, and each records these attributes.
    [Fact]
    public async Task TheMetricTableOnThePage_IsTheInstrumentsTheMeterPublishes()
    {
        using var listener = new DecisionTelemetryListener();
        await RunAsync(Typed, Options(), Reply.Ok(TicketResponse));
        await RunAsync(Typed, Options(), Reply.Error(422));
        await RunAsync(Listing, Options(), Reply.Ok(ModelsResponse));

        var rows = PageTables.Rows(Page, "Metrics");

        Assert.Equal(6, listener.Instruments.Count);
        Assert.Equal(listener.Instruments.Keys.Order(StringComparer.Ordinal), rows.Select(row => PageTables.Code(row[0])).Order(StringComparer.Ordinal));
        Assert.Equal(
            rows.Select(row => $"{row[1]} {PageTables.Code(row[2])}"),
            rows.Select(row => $"{KindOf(listener.Instruments[PageTables.Code(row[0])])} {listener.Instruments[PageTables.Code(row[0])].Unit}"));
        Assert.Equal(
            rows.Select(row => string.Join(',', Attributes(listener, PageTables.Code(row[0])))),
            rows.Select(row => string.Join(',', new SortedSet<string>(PageTables.AllCode(row[3]), StringComparer.Ordinal))));
    }

    private static string KindOf(Instrument instrument)
        => instrument switch
        {
            Histogram<double> or Histogram<long> => "Histogram",
            Counter<long> or Counter<double> => "Counter",
            _ => instrument.GetType().Name,
        };

    [Fact]
    public async Task TheBucketAdviceOnThePage_IsTheInstrumentsAdvice()
    {
        using var listener = new DecisionTelemetryListener();
        await RunAsync(Typed, Options(), Reply.Ok(TicketResponse));
        var page = PageTables.Text(Page);

        var duration = Buckets(listener, "gen_ai.client.operation.duration");
        var input = Buckets(listener, "gen_ai.client.inference.operation.input_tokens");
        var output = Buckets(listener, "gen_ai.client.inference.operation.output_tokens");
        var confidence = Buckets(listener, "minos.answer.confidence");

        Assert.Equal(14, duration.Length);
        Assert.True(EveryStepIs(duration, 2));
        Assert.Contains($"from `{Show(duration[0])}` to `{Show(duration[^1])}` seconds, doubling", page, StringComparison.Ordinal);
        Assert.Equal(input, output);
        Assert.Equal(14, input.Length);
        Assert.True(EveryStepIs(input, 4));
        Assert.Contains($"from `{Show(input[0])}` to `{Show(input[^1])}`, four times", page, StringComparison.Ordinal);
        Assert.Equal([0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.99], confidence);
        Assert.Contains("`0.1` to `0.9` in steps of `0.1`, then `0.95` and `0.99`", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confidence_IsOnePointForEachChoiceAndScoreAnswer_AndNoneForNoul()
    {
        using var listener = new DecisionTelemetryListener();

        await RunAsync(Typed, Options(), Reply.Ok(TicketResponse));
        Assert.Equal([0.64, 0.7], Points(listener, "minos.answer.confidence").Select(point => point.Value).Order());

        listener.Measurements.Clear();
        await RunAsync(
            client => client.EvaluateAsync<ObservedUrgency>("Help!", CancellationToken.None).AsTask(),
            Options(),
            Reply.Ok(UrgentResponse));
        Assert.Empty(Points(listener, "minos.answer.confidence"));
    }

    [Fact]
    public async Task TheDuration_CarriesTheResponseModelOnSuccess_AndTheErrorTypeOnFailure_NeverBoth()
    {
        using var listener = new DecisionTelemetryListener();
        await RunAsync(Typed, Options(), Reply.Ok(TicketResponse));
        await RunAsync(Typed, Options(), Reply.Error(422));
        await RunAsync(Listing, Options(), Reply.Ok(ModelsResponse));
        await RunAsync(Listing, Options(), Reply.Error(500));

        var durations = Points(listener, "gen_ai.client.operation.duration");

        Assert.Equal(4, durations.Length);
        Assert.Equal(
            ["gen_ai.operation.name,gen_ai.provider.name,gen_ai.request.model,gen_ai.response.model,server.address,server.port",
             "error.type,gen_ai.operation.name,gen_ai.provider.name,gen_ai.request.model,server.address,server.port",
             "gen_ai.operation.name,gen_ai.provider.name,server.address,server.port",
             "error.type,gen_ai.operation.name,gen_ai.provider.name,server.address,server.port"],
            durations.Select(point => string.Join(',', Keys(point.Tags).Order(StringComparer.Ordinal))));
        Assert.Equal("Validation", Value(durations[1].Tags, "error.type"));
        Assert.Equal("Server", Value(durations[3].Tags, "error.type"));
    }

    [Fact]
    public async Task Counters_AddTheTokens_WithATextModality()
    {
        using var listener = new DecisionTelemetryListener();

        await RunAsync(Typed, Options(), Reply.Ok(TicketResponse));

        var input = Only.Of(Points(listener, "gen_ai.client.inference.usage.input_tokens"));
        var output = Only.Of(Points(listener, "gen_ai.client.inference.usage.output_tokens"));
        Assert.Equal(150, input.Value);
        Assert.Equal(20, output.Value);
        Assert.Equal("text", Value(input.Tags, "gen_ai.token.modality"));
    }

    [Fact]
    public async Task NoSpanAndNoMetric_CarriesTheState_TheKey_OrTheServersErrorBody()
    {
        using var listener = new DecisionTelemetryListener();
        var options = Options();
        options.ApiKey = "SECRET-KEY-VALUE";
        await RunAsync(Typed, options, Reply.Ok(TicketResponse));
        await RunAsync(Typed, options, Reply.Error(422, "{\"detail\":\"SECRET-ERROR-BODY\"}"));
        await RunAsync(Raw, options, Reply.Ok("SECRET-ERROR-BODY not json"));
        await RunAsync(Typed, options, Reply.Refused);

        var seen = new StringBuilder();
        foreach (var span in listener.Spans)
        {
            seen.Append(span.DisplayName).Append(span.OperationName).Append(span.StatusDescription);
            seen.Append(Describe(span.TagObjects));
        }

        foreach (var point in listener.Measurements)
        {
            seen.Append(Describe(point.Tags));
        }

        Assert.Equal(4, listener.Spans.Count);
        Assert.DoesNotContain("SECRET-STATE", seen.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-KEY-VALUE", seen.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-ERROR-BODY", seen.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("refused", seen.ToString(), StringComparison.Ordinal);
    }
}
