using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Minos.Shared;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace Minos.AotSmoke;

/// <summary>
/// Allocation budgets for the hot paths the smoke app exercises, measured under the published Native AOT binary.
/// Each gate runs <see cref="AllocationGate"/> for 1000 iterations and turns a budget breach into a failed
/// <c>Check</c>, rather than letting <see cref="AllocationGate"/>'s exception crash the whole run.
/// </summary>
internal static class AllocationChecks
{
    // The iteration count of every synchronous gate, the relative ones included.
    private const int GateIterations = 1000;

    private const string NoulResponseJson = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    private const string TriageResponseJson = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    /// <summary>
    /// <c>AnswerSlots.Noul</c>, <c>Choice</c> and <c>Score</c> over the slots a protocol read from the triage answers.
    /// The slots are a ref struct the protocol owns, so the gate runs inside a hand-written <c>Create</c> that
    /// <see cref="DecisionClientExtensions.EvaluateAsync{T}(IDecisionClient, string)"/> calls, and counts only the allocations of the accessor loops.
    /// </summary>
    public static async Task AnswerSlotAccessors()
    {
        await RunSlotProbeAsync().ConfigureAwait(false);

        // Measured 0 B/call on published win-x64 AOT for each accessor: Noul reads a double off the slot span, and
        // Choice and Score return structs over the protocol's probability array. Each gate keeps the 0 B budget of the
        // public answer reader that read the same answer before Phase 6.2, so none may allocate.
        const long BudgetBytes = 0;
        Program.Check(
            SlotProbe.NoulBytes is >= 0 and <= BudgetBytes,
            $"AnswerSlots.Noul stays within its allocation budget: {SlotProbe.NoulBytes} B over {GateIterations} calls against {BudgetBytes} B");
        Program.Check(
            SlotProbe.ChoiceBytes is >= 0 and <= BudgetBytes,
            $"AnswerSlots.Choice stays within its allocation budget: {SlotProbe.ChoiceBytes} B over {GateIterations} calls against {BudgetBytes} B");
        Program.Check(
            SlotProbe.ScoreBytes is >= 0 and <= BudgetBytes,
            $"AnswerSlots.Score stays within its allocation budget: {SlotProbe.ScoreBytes} B over {GateIterations} calls against {BudgetBytes} B");
    }

    /// <summary>
    /// The generated <c>SmokeTriage.Create</c> over the slots a protocol read from the triage answers: the result record,
    /// which holds its typed answers inline. Together with the protocol's own probability buffer, which the typed
    /// evaluation gates count, it is the work the old generated <c>Parse</c> gate held to 192 B.
    /// </summary>
    public static async Task GeneratedCreate()
    {
        await RunSlotProbeAsync().ConfigureAwait(false);

        // Measured 112 B/call on published win-x64 AOT: the SmokeTriage record alone, whose Noul, Choice and Score
        // answers are structs held inline over the protocol's probability buffer, which is allocated outside the loop.
        // Budget: about 10% headroom over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8
        // rule, and below the 192 B of the generated Parse it replaces.
        const long BudgetBytes = 128;
        Program.Check(
            SlotProbe.CreateBytes >= 0 && SlotProbe.CreateBytes <= BudgetBytes * GateIterations,
            $"SmokeTriage.Create stays within its allocation budget: {SlotProbe.CreateBytes} B over {GateIterations} calls against {BudgetBytes} B/call");
    }

    private static async Task RunSlotProbeAsync()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });

        var result = await client.EvaluateAsync<SlotProbe>(SmokeAnswers.State).ConfigureAwait(false);
        Program.Check(result.IsSuccess, "the slot probe is created over the triage answers");
    }

    /// <summary><see cref="DecisionClient.EvaluateAsync"/> over a canned handler: request serialization and response
    /// deserialization through the public API only, since <c>DecisionJsonContext</c> is internal.</summary>
    public static void EvaluateRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, NoulResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var request = new SystemOneRequest
        {
            State = SmokeAnswers.State,
            Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        };

        // Measured 3928 B/call on published win-x64 AOT: HttpRequestMessage, headers and content for the request,
        // plus the response's HttpResponseMessage, its body buffering and JSON deserialization into
        // SystemOneResponse. It measured 4312 B/call before ZeroAlloc.Rest 3.2.1 and 4592 B/call when first budgeted in
        // Phase 1.8; 3.2.1's generated transport no longer allocates a header value, boxed status codes or a tag array
        // per call. Budget: about 10% headroom over the measurement, rounded up to the next multiple of 64 B, per the
        // Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 4352,
            action: () => client.EvaluateAsync(request),
            label: "EvaluateRoundTrip",
            passDescription: "EvaluateAsync stays within its allocation budget");
    }

    /// <summary><see cref="DecisionClientExtensions.EvaluateAsync{T}(IDecisionClient, string)"/> over a canned handler: the raw, pooled-buffer
    /// path, through the public API only, since <c>TypedEvaluation</c> and <c>RawJson</c> are internal.</summary>
    public static void TypedEvaluateRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });

        // Measured 3424 B/call on published win-x64 AOT through the standard pipeline: the request's Utf8JsonWriter and
        // RawJson, ZeroAlloc.Rest's own per-attempt allocations (HttpRequestMessage, headers, the MemoryStream the body is
        // copied into, and StreamContent), the response's HttpResponseMessage and body buffering, the DecisionRequest, the
        // DecisionResponse with its AnswerSlot[] and probabilities, and the SmokeTriage. About 176 B of it is ZeroAlloc.Rest
        // 3.3.0's __EscapePath, a StringBuilder and a string for the {**path} route on every call, fixed by
        // ZeroAlloc.Rest#425 in 3.3.1; without it the call measures about 3248 B. It measured 2984 B/call before the
        // pipeline, 3368 B/call before ZeroAlloc.Rest 3.2.1 and 3784 B/call when first budgeted in Phase 1.8. Budget: about
        // 10% headroom over the 2984 B measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 3328,
            action: () => client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State),
            label: "TypedEvaluateRoundTrip",
            passDescription: "EvaluateAsync<T> stays within its allocation budget");
    }

    /// <summary>The neutral call on the standard pipeline over a canned handler, nothing listening.</summary>
    public static void NeutralEvaluateRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);

        // Measured 3248 B/call on published win-x64 AOT on ZeroAlloc.Rest 3.3.0: the request's Utf8JsonWriter and
        // RawJson, ZeroAlloc.Rest's per-attempt HttpRequestMessage, headers, MemoryStream and StreamContent, the
        // response's HttpResponseMessage and body buffering, and the DecisionResponse with its AnswerSlot[] and
        // probabilities. The standard stages add nothing on a call that completes synchronously with nothing listening,
        // so it measures the same as BareTransportRoundTrip. 176 B of it is 3.3.0's __EscapePath for the {**path}
        // route, fixed by ZeroAlloc.Rest#425 in 3.3.1; a constant-route build measures 3072 B/call. Budget: about 10%
        // headroom over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 3584,
            action: () => client.EvaluateAsync(request),
            label: "NeutralEvaluateRoundTrip",
            passDescription: "EvaluateAsync of a DecisionRequest stays within its allocation budget");
    }

    /// <summary><see cref="NeutralEvaluateRoundTrip"/> while discarding listeners sample every span and enable every instrument.</summary>
    public static void NeutralEvaluateRoundTripWhileListening()
    {
        using var telemetry = new DiscardingTelemetry();
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);

        // Measured 4616 B/call on published win-x64 AOT on ZeroAlloc.Rest 3.3.0: NeutralEvaluateRoundTrip's bytes plus
        // the Activity, its boxed start tags, the boxed tag and measurement values and each metric's TagList. 176 B of
        // it is 3.3.0's __EscapePath, fixed in 3.3.1; a constant-route build measures 4440 B/call. Budget: about 10%
        // headroom over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 5120,
            action: () => client.EvaluateAsync(request),
            label: "NeutralEvaluateRoundTripWhileListening",
            passDescription: "EvaluateAsync of a DecisionRequest while listening stays within its allocation budget");
        Program.Check(telemetry.Measurements > 0, "the discarding listeners received measurements, so the neutral call ran the listening path");
        Program.Check(telemetry.StoppedSpans > 0, "the discarding listeners recorded spans, so the neutral call ran the listening path");
    }

    /// <summary><see cref="NeutralEvaluateRoundTrip"/> with <see cref="DecisionClientOptions.UseStandardPipeline"/> off: the transport alone.</summary>
    public static void BareTransportRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key", UseStandardPipeline = false });
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);

        // Measured 3248 B/call on published win-x64 AOT on ZeroAlloc.Rest 3.3.0, the same as NeutralEvaluateRoundTrip:
        // the transport's request and response, body buffering and the DecisionResponse, with no stage around it. 176 B
        // of it is 3.3.0's __EscapePath, fixed in 3.3.1; a constant-route build measures 3072 B/call. Budget: about 10%
        // headroom over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 3584,
            action: () => client.EvaluateAsync(request),
            label: "BareTransportRoundTrip",
            passDescription: "EvaluateAsync of a DecisionRequest on the bare transport stays within its allocation budget");
    }

    /// <summary>A <see cref="DelegatingDecisionClient"/> with no overrides over an inner client whose call completes synchronously.</summary>
    public static void PassThroughStage()
    {
        using var stage = new PipelineChecks.PassThroughStage(new FixedResponseClient(PipelineChecks.TriageResponse()));
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);

        // Measured 0 B/call on published win-x64 AOT, and budgeted at exactly 0 B: the base class returns the inner
        // client's completed ValueTask as is, so a stage that adds nothing costs nothing.
        GateValueTask(
            budgetBytes: 0,
            action: () => stage.EvaluateAsync(request),
            label: "PassThroughStage",
            passDescription: "a DelegatingDecisionClient that adds nothing allocates nothing");
    }

    /// <summary><see cref="DecisionClientExtensions.EvaluateUtf8Async{T}(IDecisionClient, ReadOnlyMemory{byte})"/> over a canned handler.</summary>
    public static void Utf8StateEvaluateRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        ReadOnlyMemory<byte> state = Encoding.UTF8.GetBytes(SmokeAnswers.JsonState);

        // Measured 3704 B/call on published win-x64 AOT on ZeroAlloc.Rest 3.3.0: TypedEvaluateRoundTrip's 3424 B plus
        // the JsonDocument DecisionContent.FromUtf8Json parses the state into, 280 B, which the string and JsonElement
        // overloads do not pay. 176 B of it is 3.3.0's __EscapePath, fixed in 3.3.1; a constant-route build measures
        // 3528 B/call. Budget: about 10% headroom over the measurement, rounded up to the next multiple of 64 B, per
        // the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 4096,
            action: () => client.EvaluateUtf8Async<SmokeTriage>(state),
            label: "Utf8StateEvaluateRoundTrip",
            passDescription: "EvaluateUtf8Async<T> stays within its allocation budget");
    }

    /// <summary><see cref="DecisionClientExtensions.EvaluateAsync{T, TState}(IDecisionClient, TState, System.Text.Json.Serialization.Metadata.JsonTypeInfo{TState})"/> over a canned handler.</summary>
    public static void TypedStateEvaluateRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, Program.CredentialsResponse)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var state = new SmokeState("Payouts failing", SmokeAnswers.State);

        // Measured 3008 B/call on published win-x64 AOT on ZeroAlloc.Rest 3.3.0, over a one-question response: the
        // transport's request and response, the DecisionResponse, the SmokeStateTriage, and the JsonDocument
        // DecisionContent.FromValue serializes the state into, which ContentFromValue measures at 280 B for the same
        // state and the string and JsonElement overloads do not pay. 176 B of it is 3.3.0's __EscapePath, fixed in
        // 3.3.1; a constant-route build measures 2832 B/call. Budget: about 10% headroom over the measurement, rounded
        // up to the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 3328,
            action: () => client.EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState),
            label: "TypedStateEvaluateRoundTrip",
            passDescription: "EvaluateAsync<T, TState> stays within its allocation budget");
    }

    /// <summary><see cref="EvaluateRoundTrip"/> through <see cref="NullLoggerFactory"/>, whose logger has every level disabled.</summary>
    public static void EvaluateRoundTripWithNullLoggerFactory()
    {
        // Budget: EvaluateRoundTrip's own, 4352 B, unchanged. With no level enabled the client takes the unlogged path:
        // no logging stage and no logging state machine, so nothing may be added.
        EvaluateRoundTripThrough(
            NullLoggerFactory.Instance,
            budgetBytes: 4352,
            "EvaluateRoundTripWithNullLoggerFactory",
            "EvaluateAsync through NullLoggerFactory stays within EvaluateRoundTrip's budget");
    }

    /// <summary>
    /// <see cref="TypedEvaluateRoundTrip"/> through a real <see cref="LoggerFactory"/> with a provider, whose filter
    /// disables every level.
    /// </summary>
    public static void TypedEvaluateRoundTripWithEveryLevelFiltered()
    {
        using var provider = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([provider], new LoggerFilterOptions { MinLevel = LogLevel.None });

        // Budget: TypedEvaluateRoundTrip's own, 3328 B, unchanged, for the same reason as the NullLoggerFactory gate.
        TypedEvaluateRoundTripThrough(
            factory,
            budgetBytes: 3328,
            "TypedEvaluateRoundTripWithEveryLevelFiltered",
            "EvaluateAsync<T> with every level filtered out stays within TypedEvaluateRoundTrip's budget");
        Program.Check(provider.Records.Length == 0, "a logger with every level filtered out receives no record");
    }

    /// <summary>
    /// <see cref="EvaluateRoundTrip"/> through a logger enabled at every level that discards everything, so every log
    /// call, timestamp and wrapper runs.
    /// </summary>
    public static void EvaluateRoundTripWithDiscardingLogger()
    {
        var callsBefore = DiscardingLoggerFactory.Calls;

        // Measured 3928 B/call on published win-x64 AOT, the same as EvaluateRoundTrip's current measurement and its unlogged twin in this run,
        // so the enabled logger adds nothing on the synchronous path the canned handler takes: each event's state is a
        // struct handed to a logger that discards it, the timing is two Stopwatch timestamps, and no state machine is
        // boxed while a call completes synchronously. A call that completes asynchronously also allocates the
        // logging state machines, which this gate cannot see. Budget: about 10% headroom
        // over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        EvaluateRoundTripThrough(
            DiscardingLoggerFactory.Instance,
            budgetBytes: 4352,
            "EvaluateRoundTripWithDiscardingLogger",
            "EvaluateAsync with every log level enabled stays within its budget");
        Program.Check(
            DiscardingLoggerFactory.Calls > callsBefore,
            "the discarding logger received log calls, so EvaluateAsync ran the logged path");
    }

    /// <summary><see cref="TypedEvaluateRoundTrip"/> through a logger enabled at every level that discards everything.</summary>
    public static void TypedEvaluateRoundTripWithDiscardingLogger()
    {
        var callsBefore = DiscardingLoggerFactory.Calls;

        // Measured 3424 B/call on published win-x64 AOT, the same as TypedEvaluateRoundTrip's current measurement and its unlogged twin in this run,
        // so the enabled logger adds nothing on the synchronous path the canned handler takes: each event's state is a
        // struct handed to a logger that discards it, the timing is two Stopwatch timestamps, and no state machine is
        // boxed while a call completes synchronously. A call that completes asynchronously also allocates the
        // logging state machines, which this gate cannot see. Budget: about 10% headroom
        // over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        TypedEvaluateRoundTripThrough(
            DiscardingLoggerFactory.Instance,
            budgetBytes: 3328,
            "TypedEvaluateRoundTripWithDiscardingLogger",
            "EvaluateAsync<T> with every log level enabled stays within its budget");
        Program.Check(
            DiscardingLoggerFactory.Calls > callsBefore,
            "the discarding logger received log calls, so EvaluateAsync<T> ran the logged path");
    }

    /// <summary>
    /// Proves the disabled-logger gates can tell a disabled logger from an enabled one. The canned handler completes
    /// synchronously, where even an enabled logger adds nothing, so those gates would pass if a disabled logger wrongly
    /// entered the logging wrappers. A yielding handler makes every call complete asynchronously, where the wrappers'
    /// state machines are boxed and show up as bytes.
    /// </summary>
    public static async Task DisabledLoggerAddsNothingWhereAnEnabledOneDoes()
    {
        var request = Program.Request();
        Func<DecisionClient, ValueTask<Result<SystemOneResponse, DecisionError>>> evaluate = client => client.EvaluateAsync(request);
        var unlogged = await MedianYieldingAsync(NoulResponseJson, null, evaluate).ConfigureAwait(false);
        var disabled = await MedianYieldingAsync(NoulResponseJson, NullLoggerFactory.Instance, evaluate).ConfigureAwait(false);
        var enabled = await MedianYieldingAsync(NoulResponseJson, DiscardingLoggerFactory.Instance, evaluate).ConfigureAwait(false);
        Console.WriteLine($"     yielding EvaluateAsync B/call: no factory {unlogged}, NullLoggerFactory {disabled}, discarding logger {enabled}");

        // The tolerance absorbs the spread that remains once the pools are stocked. It is observed, not attributed: a
        // settled window still moves by 904 to 1384 B, about 2 to 3 B/call, from run to run. The counter has to be
        // process-wide, since the continuations hop pool threads, so it would also count any runtime thread's allocation.
        // Over 800 fresh runs of the published win-x64 app on 2026-10-07, the medians of the two sides differed by -7 to
        // +10 B/call, 95% of them within -2 to +2. An earlier 8 B tolerance failed the one run at +10, so the tolerance
        // is 16 B: above the largest excess seen, and far below what it guards against, since a logging wrapper's state
        // machine adds hundreds of bytes per call. #104 tracks the tolerance and this helper's replacement.
        Program.Check(
            disabled - unlogged <= 16,
            "a NullLoggerFactory adds no allocation to an asynchronously completing EvaluateAsync");
        Program.Check(
            enabled - unlogged > 0,
            "the discarding logger adds allocation to an asynchronously completing EvaluateAsync, so this check sees the wrappers");
    }

    // The median of five runs. Yielding runs vary in both directions: a runtime thread allocating during the loop adds
    // bytes, and one that allocated less than usual saves some. The least of several runs is therefore biased low and
    // lets one lucky run set a baseline; the median ignores an outlier on either side.
    private static async Task<long> MedianYieldingAsync<TResult>(
        string responseJson, ILoggerFactory? loggerFactory, Func<DecisionClient, ValueTask<TResult>> call)
    {
        const int Runs = 5;
        var runs = new List<long>(Runs);
        for (var run = 0; run < Runs; run++)
        {
            runs.Add(await MeasureYieldingAsync(responseJson, loggerFactory, call).ConfigureAwait(false));
        }

        runs.Sort();
        return runs[Runs / 2];
    }

    // Bytes allocated per awaited call over a handler that yields, on any thread, since the continuation does not run on
    // the caller's. The loop is sequential, so nothing else allocates meanwhile but the runtime. call is created once by
    // the caller, so invoking it allocates nothing per call. ZeroAlloc.TestHelpers has no equivalent: its ValueTask
    // measurements throw unless every call completes synchronously, and they count the calling thread only.
    //
    // The order matters, part of #79. A forced gen2 GC runs ArrayPool.Shared's trim, which under high machine memory load
    // drops every pooled array, so the collections come first and wait for that trim, and the warm-up comes after them; a
    // collection after the warm-up made the window pay for the refill, about 41 B/call. The warm-up is long because the
    // pool keeps one array per size in each thread's own slot, and the continuations rotate over several pool threads: 100
    // calls left some threads unstocked, so a window still rented fresh 16 KB arrays, 0 to 2 per window, up to 66 B/call.
    private static async Task<long> MeasureYieldingAsync<TResult>(
        string responseJson, ILoggerFactory? loggerFactory, Func<DecisionClient, ValueTask<TResult>> call)
    {
        const int WarmupIterations = 2000;
        const int YieldingIterations = 500;
        using var http = new HttpClient(new YieldingHandler(HttpStatusCode.OK, responseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" }, loggerFactory);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = await call(client).ConfigureAwait(false);
        }

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < YieldingIterations; i++)
        {
            _ = await call(client).ConfigureAwait(false);
        }

        return (GC.GetTotalAllocatedBytes(precise: true) - before) / YieldingIterations;
    }

    // EvaluateRoundTrip's call, the same canned response and Program.Request(), through a logging client.
    private static void EvaluateRoundTripThrough(ILoggerFactory loggerFactory, int budgetBytes, string label, string passDescription)
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, NoulResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" }, loggerFactory);
        var request = Program.Request();

        GateValueTask(budgetBytes, () => client.EvaluateAsync(request), label, passDescription);
    }

    // TypedEvaluateRoundTrip's call, the same canned response and state, through a logging client.
    private static void TypedEvaluateRoundTripThrough(ILoggerFactory loggerFactory, int budgetBytes, string label, string passDescription)
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" }, loggerFactory);

        GateValueTask(
            budgetBytes,
            () => client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State),
            label,
            passDescription);
    }

    /// <summary><see cref="DecisionContent.FromValue{T}(T, System.Text.Json.Serialization.Metadata.JsonTypeInfo{T})"/> over the smoke state.</summary>
    [Covers("static Minos.DecisionContent.FromValue<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>! typeInfo) -> Minos.DecisionContent")]
    public static void ContentFromValue()
    {
        var state = new SmokeState("Payouts failing", SmokeAnswers.State);

        // Measured 280 B/call on published win-x64 AOT: JsonSerializer.SerializeToElement serializes SmokeState's
        // Subject and Body strings and builds a JsonDocument over the result, which DecisionContent then wraps without
        // copying. A linux-x64 measurement (dotnet/sdk:10.0 container, 2026-09-28) matches exactly: 280 B/call. The
        // budget keeps about 10% headroom (320 B, rounded to the next 64 B) over that measurement, since
        // JsonDocument's internal buffer sizing can still differ across runtime patch versions on either platform.
        Gate(
            budgetBytes: 320,
            action: () => _ = DecisionContent.FromValue(state, SmokeStateJsonContext.Default.SmokeState),
            label: "ContentFromValue",
            passDescription: "DecisionContent.FromValue stays within its allocation budget");
    }

    /// <summary><see cref="DecisionContent.FromUtf8Json(ReadOnlySpan{byte})"/> over a fixed object.</summary>
    [Covers("static Minos.DecisionContent.FromUtf8Json(System.ReadOnlySpan<byte> utf8Json) -> Minos.DecisionContent")]
    public static void ContentFromUtf8Json()
    {
        var json = Encoding.UTF8.GetBytes("""{"message":"Please send me your password","channel":"email"}""");

        // Measured 256 B/call on published win-x64 AOT: JsonElement.ParseValue parses the fixed JSON object into a
        // JsonDocument, which DecisionContent then wraps without copying. A linux-x64 measurement (dotnet/sdk:10.0
        // container, 2026-09-28) matches exactly: 256 B/call. The budget keeps about 10% headroom (320 B, rounded
        // to the next 64 B) over that measurement, for the same cross-platform, cross-patch-version reason as
        // ContentFromValue's gate.
        Gate(
            budgetBytes: 320,
            action: () => _ = DecisionContent.FromUtf8Json(json),
            label: "ContentFromUtf8Json",
            passDescription: "DecisionContent.FromUtf8Json stays within its allocation budget");
    }

    /// <summary><see cref="QuestionSetBuilder.Build"/> over a three-question set: a Noul, an enum Choice and a keyed Choice.</summary>
    public static void BuildQuestionSet()
    {
        var builder = SmokeBuiltSet.Builder(out _, out _, out _);

        // Measured 2648 B/call on published win-x64 AOT (6592 B/call before the questions were written on first use,
        // not at Build): the builder's question and warning lists, the question definition graph with its UTF-8 keys and
        // the resulting QuestionSet. Budget unchanged: it was about 10% headroom over 6592 B, 7251 B, rounded up to the
        // next multiple of 64, 7296 B.
        Gate(
            budgetBytes: 7296,
            action: () => _ = builder.Build(),
            label: "BuildQuestionSet",
            passDescription: "QuestionSetBuilder.Build stays within its allocation budget");
    }

    /// <summary><see cref="DecisionClientExtensions.EvaluateAsync(IDecisionClient, QuestionSet, DecisionContent)"/> over a canned handler.</summary>
    public static void EvaluateBuiltSetRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var set = SmokeBuiltSet.Full(out _, out _, out _, out _);

        // Measured 3616 B/call on published win-x64 AOT through the standard pipeline: the QuestionSet's pre-built request
        // body copied into a pooled buffer, HttpClient's request and response objects and body buffering, the
        // DecisionRequest, the DecisionResponse with its AnswerSlot[], the Answers result, and about 176 B of ZeroAlloc.Rest
        // 3.3.0's __EscapePath, fixed in 3.3.1. It measured 3272 B/call before the pipeline, 3656 B/call before ZeroAlloc.Rest 3.2.1 and
        // 4288 B/call when first budgeted in Phase 2.4. Budget: about 10% headroom over the measurement, rounded up to
        // the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 3648,
            action: () => client.EvaluateAsync(set, SmokeAnswers.State),
            label: "EvaluateBuiltSetRoundTrip",
            passDescription: "EvaluateAsync over a built set stays within its allocation budget");
    }

    private static double sink;

    /// <summary><see cref="Answers.Get(NoulHandle)"/> and its overloads, over one evaluation's answers.</summary>
    public static void AnswersGet()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        var answers = client.EvaluateAsync(set, "Help!").AsTask().GetAwaiter().GetResult().Value;

        // 0 B/call: Get rebuilds each typed answer as a struct over the answers' shared probability buffer.
        Gate(
            budgetBytes: 0,
            action: () =>
            {
                var noul = answers.Get(credentials);
                var teamAnswer = answers.Get(team);
                var productAnswer = answers.Get(product);
                var urgencyAnswer = answers.Get(urgency);

                // Consumed into a static field so the compiler cannot elide the calls and make the 0 B gate vacuous.
                sink += (noul.Value ? 1 : 0) + (int)teamAnswer.Value + productAnswer.Value.Length + (int)urgencyAnswer.Value
                    + teamAnswer.Confidence + productAnswer.Confidence + urgencyAnswer.Confidence + urgencyAnswer.Expected;
            },
            label: "AnswersGet",
            passDescription: "Answers.Get allocates nothing");
    }

    /// <summary>
    /// The pattern helpers on parsed answers: <see cref="ConfidenceThresholds.Classify"/>, <c>Score.Normalized</c> and
    /// <c>KeyedScore.Normalized</c>. Each is arithmetic over the answer struct, so the budget is 0 B.
    /// </summary>
    [Covers("Minos.ConfidenceThresholds.Classify(double confidence) -> Minos.ConfidenceTier")]
    [Covers("Minos.ConfidenceThresholds.ConfidenceThresholds(double medium, double high) -> void")]
    [Covers("Minos.QuestionSetBuilder.Score(string! key, Minos.DecisionContent instructions, out Minos.KeyedScoreHandle question, System.Action<Minos.KeyedScoreLevelsBuilder!>! configure) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.KeyedScoreLevelsBuilder.Level(Minos.Criterion! criterion) -> Minos.KeyedScoreLevelsBuilder!")]
    [Covers("Minos.Answers.Get(Minos.KeyedScoreHandle question) -> Minos.KeyedScore")]
    public static void PatternHelpers()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var set = SmokeBuiltSet.Full(out _, out _, out _, out var urgency);
        var urgencyAnswer = client.EvaluateAsync(set, "Help!").AsTask().GetAwaiter().GetResult().Value.Get(urgency);

        using var keyedHttp = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.KeyedRiskResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var keyedClient = new DecisionClient(keyedHttp, new DecisionClientOptions { ApiKey = "smoke-key" });
        var riskSet = SmokeBuiltSet.KeyedRisk(out var risk);
        var riskAnswer = keyedClient.EvaluateAsync(riskSet, "Help!").AsTask().GetAwaiter().GetResult().Value.Get(risk);

        var strict = new ConfidenceThresholds(medium: 0.6, high: 0.85);
        Program.Check(
            ConfidenceThresholds.Default.Classify(urgencyAnswer.Confidence) == ConfidenceTier.Medium
                && strict.Classify(riskAnswer.Confidence) == ConfidenceTier.High
                && ConfidenceThresholds.Default.Classify(riskAnswer.Confidence) == ConfidenceTier.Medium,
            "ConfidenceThresholds classifies parsed answers' confidence per instance under Native AOT");
        Program.Check(
            Math.Abs(urgencyAnswer.Normalized - 0.95) < 1e-9 && Math.Abs(riskAnswer.Normalized - 0.8) < 1e-9,
            "Normalized puts parsed enum and keyed Scores on a 0 to 1 scale under Native AOT");

        Gate(
            budgetBytes: 0,
            action: () =>
            {
                // Consumed into a static field so the compiler cannot elide the calls and make the 0 B gate vacuous.
                sink += (int)ConfidenceThresholds.Default.Classify(urgencyAnswer.Confidence)
                    + (int)strict.Classify(riskAnswer.Confidence)
                    + urgencyAnswer.Normalized
                    + riskAnswer.Normalized;
            },
            label: "PatternHelpers",
            passDescription: "ConfidenceThresholds.Classify and Score/KeyedScore.Normalized allocate nothing");
    }

    /// <summary>
    /// <see cref="Noul.Equals(Noul)"/> on parsed answers, directly and through <see cref="EqualityComparer{T}.Default"/>.
    /// <see cref="Noul"/> implements <see cref="IEquatable{T}"/>, so neither path boxes and the budget is 0 B.
    /// </summary>
    [Covers("Minos.Noul.Equals(Minos.Noul other) -> bool")]
    [Covers("Minos.Noul.Noul(double probability) -> void")]
    public static void NoulEquals()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var set = SmokeBuiltSet.Full(out var credentials, out _, out _, out _);
        var first = client.EvaluateAsync(set, "Help!").AsTask().GetAwaiter().GetResult().Value.Get(credentials);
        var second = client.EvaluateAsync(set, "Help!").AsTask().GetAwaiter().GetResult().Value.Get(credentials);
        var comparer = EqualityComparer<Noul>.Default;

        Program.Check(
            first == second && comparer.Equals(first, second) && first != new Noul(0.9),
            "Noul compares parsed answers by probability under Native AOT");

        Gate(
            budgetBytes: 0,
            action: () =>
            {
                // Consumed into a static field so the compiler cannot elide the calls and make the 0 B gate vacuous.
                sink += (first.Equals(second) ? 1 : 0) + (comparer.Equals(first, second) ? 1 : 0);
            },
            label: "NoulEquals",
            passDescription: "Noul.Equals allocates nothing, directly or through EqualityComparer<Noul>.Default");
    }

    /// <summary>
    /// <see cref="EvaluateRoundTrip"/>'s call through a client <c>AddDecisionClient</c> registered, against the same call on a
    /// hand-built client over an <see cref="HttpClient"/> that <see cref="DecisionClient.ConfigureHttpClient"/> configured the
    /// same way. Registration and the first resolve happen once and are not budgeted.
    /// </summary>
    [Covers("static Minos.DecisionClient.ConfigureHttpClient(System.Net.Http.HttpClient! httpClient, Minos.DecisionClientOptions? options) -> void")]
    public static void EvaluateRoundTripThroughDependencyInjection()
    {
        var services = new ServiceCollection();
        DependencyInjectionChecks.RegisterDefaultClient(services);
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!;

        // The same canned body as RegisterDefaultClient's handler, so both sides of the comparison parse one response.
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
        DecisionClient.ConfigureHttpClient(http, new DecisionClientOptions { BaseAddress = new Uri("https://example.test/api/") });
        using var handBuilt = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var request = Program.Request();

        // DI adds nothing per call: the hand-built client's own total in this run is the budget.
        RelativeGateValueTask(
            () => handBuilt.EvaluateAsync(request),
            () => resolved.EvaluateAsync(request),
            "EvaluateRoundTripThroughDependencyInjectionAgainstHandBuilt",
            "EvaluateAsync through a DI-resolved client allocates no more than through a hand-built one");

        // Measured 3992 B/call on published win-x64 AOT: EvaluateRoundTrip's call plus the User-Agent header the factory's
        // HttpClient sends, and nothing from the container or the factory, whose request logging AddDecisionClient removes.
        // Budget: about 10% headroom over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 4416,
            action: () => resolved.EvaluateAsync(request),
            label: "EvaluateRoundTripThroughDependencyInjection",
            passDescription: "EvaluateAsync through a DI-resolved client stays within its allocation budget");
    }

    /// <summary>
    /// <see cref="EvaluateRoundTripThroughDependencyInjection"/>'s comparison for a client whose options are bound from
    /// configuration. Binding and validation run once, at the first resolve, so the call itself costs what a hand-built
    /// client's does.
    /// </summary>
    public static void EvaluateRoundTripThroughBoundConfiguration()
    {
        var services = new ServiceCollection();
        DependencyInjectionChecks.RegisterBoundDefaultClient(services);
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!;

        // The bound section's base address and key, and the same canned body, so both sides make the same call.
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
        DecisionClient.ConfigureHttpClient(http, new DecisionClientOptions { BaseAddress = new Uri("https://example.test/api/") });
        using var handBuilt = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var request = Program.Request();

        RelativeGateValueTask(
            () => handBuilt.EvaluateAsync(request),
            () => resolved.EvaluateAsync(request),
            "EvaluateRoundTripThroughBoundConfigurationAgainstHandBuilt",
            "EvaluateAsync through a client bound from configuration allocates no more than through a hand-built one");
    }

    /// <summary>
    /// <see cref="EvaluateRoundTrip"/> while discarding listeners sample every span and enable every instrument.
    /// </summary>
    public static void EvaluateRoundTripWhileListening()
    {
        using var telemetry = new DiscardingTelemetry();
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, NoulResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var request = Program.Request();

        // Measured 5296 B/call on published win-x64 AOT. The call pays EvaluateRoundTrip's bytes plus the Activity, its
        // boxed start tags, the boxed tag and measurement values and each metric's TagList. Budget: about 10% headroom
        // over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 5888,
            action: () => client.EvaluateAsync(request),
            label: "EvaluateRoundTripWhileListening",
            passDescription: "EvaluateAsync while listening stays within its allocation budget");
        Program.Check(telemetry.Measurements > 0, "the discarding listeners received measurements, so EvaluateAsync ran the listening path");
        Program.Check(telemetry.StoppedSpans > 0, "the discarding listeners recorded spans, so EvaluateAsync ran the listening path");
    }

    /// <summary><see cref="TypedEvaluateRoundTrip"/> while discarding listeners are attached.</summary>
    public static void TypedEvaluateRoundTripWhileListening()
    {
        using var telemetry = new DiscardingTelemetry();
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });

        // Measured 4792 B/call on published win-x64 AOT, 4544 B/call before the pipeline. The call pays
        // TypedEvaluateRoundTrip's bytes plus the span, tags and measurements. Budget: about 10% headroom over the measurement, rounded up to the next multiple of 64 B, per the
        // Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 5056,
            action: () => client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State),
            label: "TypedEvaluateRoundTripWhileListening",
            passDescription: "EvaluateAsync<T> while listening stays within its allocation budget");
        Program.Check(telemetry.Measurements > 0, "the discarding listeners received measurements, so EvaluateAsync<T> ran the listening path");
        Program.Check(telemetry.StoppedSpans > 0, "the discarding listeners recorded spans, so EvaluateAsync<T> ran the listening path");
    }

    /// <summary><see cref="EvaluateBuiltSetRoundTrip"/> while discarding listeners are attached.</summary>
    public static void EvaluateBuiltSetRoundTripWhileListening()
    {
        using var telemetry = new DiscardingTelemetry();
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });
        var set = SmokeBuiltSet.Full(out _, out _, out _, out _);

        // Measured 4984 B/call on published win-x64 AOT, 4832 B/call before the pipeline. The call pays
        // EvaluateBuiltSetRoundTrip's bytes plus the span,
        // tags and measurements. Budget: about 10% headroom over the measurement, rounded up to the next multiple of 64 B,
        // per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 5376,
            action: () => client.EvaluateAsync(set, SmokeAnswers.State),
            label: "EvaluateBuiltSetRoundTripWhileListening",
            passDescription: "EvaluateAsync over a built set while listening stays within its allocation budget");
        Program.Check(telemetry.Measurements > 0, "the discarding listeners received measurements, so the built set ran the listening path");
        Program.Check(telemetry.StoppedSpans > 0, "the discarding listeners recorded spans, so the built set ran the listening path");
    }

    /// <summary>
    /// A typed evaluation that completes asynchronously, with nothing listening: the one place telemetry adds bytes when
    /// off, the stages' state machines. The canned-handler gates complete synchronously and cannot see it.
    /// It replaced OperationsCostTests' in-process unwrap check, so <c>dotnet test</c> has no counterpart.
    /// </summary>
    public static async Task TelemetryOffAsynchronousTypedEvaluation()
    {
        var median = await MedianYieldingAsync(TriageResponseJson, null, static client => client.EvaluateAsync<SmokeTriage>("Help!")).ConfigureAwait(false);
        Console.WriteLine($"     yielding EvaluateAsync<T> B/call with telemetry off: {median}");

        // Measured 4565 B/call, the median of five runs, on published win-x64 AOT, 4184 B/call before the pipeline. This is
        // the whole asynchronously completing call: the transport's request and response, body buffering, parsing, the
        // DecisionRequest and DecisionResponse, ZeroAlloc.Rest 3.3.0's __EscapePath (about 176 B) and every boxed async state
        // machine, among them the retry stage's and the typed extension's. Budget: about 10% headroom over the measurement, rounded up
        // to the next multiple of 64 B, per the Phase 1.8 rule.
        const long BudgetBytes = 4608;
        Program.Check(
            median <= BudgetBytes,
            $"an asynchronously completing EvaluateAsync<T> with telemetry off stays within its allocation budget: {median} B/call against {BudgetBytes} B");
    }

    private static void Gate(int budgetBytes, Action action, string label, string passDescription)
    {
        try
        {
            AllocationGate.AssertBudget(budgetBytes, GateIterations, action, label);
            Program.Check(true, passDescription);
        }
        catch (InvalidOperationException exception)
        {
            Program.Check(false, exception.Message);
        }
    }

    private static void GateValueTask<T>(int budgetBytes, Func<ValueTask<T>> action, string label, string passDescription)
    {
        try
        {
            AllocationGate.AssertBudgetValueTask(budgetBytes, GateIterations, action, label);
            Program.Check(true, passDescription);
        }
        catch (InvalidOperationException exception)
        {
            Program.Check(false, exception.Message);
        }
    }

    // A relative gate: candidate's total over GateIterations calls may not exceed baseline's, both measured in this run.
    // AllocationGate compares the totals, so neither side is rounded and there is no headroom to hide a byte. Since
    // ZeroAlloc.TestHelpers 1.5.1 it settles the heap before its warm-up, so both totals are the calls' true cost, the
    // same in every run; 1.5.0 collected after the warm-up and measured a refill of the pools that collection emptied,
    // which made these gates flaky, #79.
    private static void RelativeGateValueTask<T>(
        Func<ValueTask<T>> baseline, Func<ValueTask<T>> candidate, string label, string passDescription)
    {
        try
        {
            AllocationGate.AssertNoMoreThanValueTask(GateIterations, baseline, candidate, label);
            Program.Check(true, passDescription);
        }
        catch (InvalidOperationException exception)
        {
            Program.Check(false, exception.Message);
        }
    }

    /// <summary>
    /// A hand-written question set over the triage questions whose <c>Create</c> measures the accessors of the
    /// <see cref="AnswerSlots"/> it is handed.
    /// </summary>
    private sealed class SlotProbe : IQuestionSet<SlotProbe>
    {
        private static int sink;

        public static long NoulBytes { get; private set; } = -1;

        public static long ChoiceBytes { get; private set; } = -1;

        public static long ScoreBytes { get; private set; } = -1;

        public static long CreateBytes { get; private set; } = -1;

        public static QuestionSetDefinition Definition => SmokeTriage.Definition;

        public static SlotProbe Create(AnswerSlots answers)
        {
            var teams = TeamOptions.Instance;
            var urgencies = UrgencyOptions.Instance;
            var total = 0;

            // Warm-up, so first-call work such as static construction is not counted.
            for (var i = 0; i < GateIterations; i++)
            {
                total += (answers.Noul(0).Value ? 1 : 0) + (int)answers.Choice(1, teams).Value + (int)answers.Score(2, urgencies).Value;
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < GateIterations; i++)
            {
                total += answers.Noul(0).Value ? 1 : 0;
            }

            var afterNoul = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < GateIterations; i++)
            {
                total += (int)answers.Choice(1, teams).Value;
            }

            var afterChoice = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < GateIterations; i++)
            {
                total += (int)answers.Score(2, urgencies).Value;
            }

            var afterScore = GC.GetAllocatedBytesForCurrentThread();
            var beforeCreate = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < GateIterations; i++)
            {
                total += SmokeTriage.Create(answers).RequestsCredentials.Value ? 1 : 0;
            }

            CreateBytes = GC.GetAllocatedBytesForCurrentThread() - beforeCreate;
            NoulBytes = afterNoul - before;
            ChoiceBytes = afterChoice - afterNoul;
            ScoreBytes = afterScore - afterChoice;
            sink = total;
            return new SlotProbe();
        }
    }

    /// <summary>Mirrors the generated option set for <see cref="Team"/>, since the real one is a private nested class.</summary>
    private sealed class TeamOptions : DecisionOptionSet<Team>
    {
        public static readonly TeamOptions Instance = new();

        public override int Count => 2;

        public override Team this[int index] => index switch
        {
            0 => Team.Billing,
            1 => Team.Account,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public override int IndexOf(Team value) => value switch
        {
            Team.Billing => 0,
            Team.Account => 1,
            _ => -1,
        };
    }

    /// <summary>Mirrors the generated option set for <see cref="Urgency"/>, since the real one is a private nested class.</summary>
    private sealed class UrgencyOptions : DecisionOptionSet<Urgency>
    {
        public static readonly UrgencyOptions Instance = new();

        public override int Count => 3;

        public override Urgency this[int index] => index switch
        {
            0 => Urgency.Low,
            1 => Urgency.Medium,
            2 => Urgency.High,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public override int IndexOf(Urgency value) => value switch
        {
            Urgency.Low => 0,
            Urgency.Medium => 1,
            Urgency.High => 2,
            _ => -1,
        };
    }
}
