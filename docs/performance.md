---
id: performance
title: Performance
sidebar_position: 14
description: What the client costs per call, how it compares with other clients, and how to run the benchmarks.
---

# Performance

`benchmarks/Minos.NET.Benchmarks` measures the client's hot paths with BenchmarkDotNet: the
protocol's reading of the answers into slots plus the generated `[Questions]` `Create` method
(`ParseBenchmarks`), `DecisionClient.EvaluateAsync` / `ListModelsAsync` against an in-memory
`HttpMessageHandler` (`ClientBenchmarks`) and question sets built at run time (`QuestionSetBenchmarks`).

Run it locally with:

```
dotnet run -c Release --project benchmarks/Minos.NET.Benchmarks -- --filter '*'
```

or trigger the manual **Benchmarks** GitHub Actions workflow's `full` job, which uploads the
`BenchmarkDotNet.Artifacts` results.

## Baseline

To be recorded from the first full run.

The phase sections below record each phase's figures as measured then. Where a figure has since changed, today's value is
given in parentheses, and the table under [Phase 5.1](#phase-51--public-api-review) lists the AOT gates after ZeroAlloc.Rest 3.2.1.
[Phase 6.3](#phase-63--the-client-pipeline) has the figures after the client pipeline.

### Phase 2.3 — DecisionContent factories

| Benchmark | Mean | Allocated | AOT smoke budget |
|---|---|---|---|
| `ContentBenchmarks.FromValue` | 538.7 ns | 256 B | 320 B |
| `ContentBenchmarks.FromUtf8Json` | 708.7 ns | 256 B | 320 B |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401, with
`--job short`. Question sets add no cost per call after the first: `Examples` and `NotFor` change only the
questions object, which is written once per set from its definition and cached.

The means come from `--job short`, which runs few iterations and leaves wide error bars; treat them as
indicative only. The first full run supersedes them.

The AOT smoke gates measure on their own inputs, not the benchmark's. The `FromValue` gate serializes the
smoke app's state and measures 280 B per call, where the benchmark shows 256 B, so its 320 B budget is that
280 B plus about 10%, rounded up to the next 64 B. The `FromUtf8Json` gate measures the same 256 B as the
benchmark, and the same rounding gives it 320 B too.

### Phase 2.4 — Question sets built at run time

| Benchmark | Mean | Allocated | Budget |
|---|---|---|---|
| `QuestionSetBenchmarks.Build` | 1.903 us | 6728 B | 7296 B (AOT smoke, a different three-question set, unchanged) |
| `QuestionSetBenchmarks.EvaluateBuiltSet` | 4.234 us | 3808 B | 3648 B today, 4736 B then (AOT smoke, a different set) |
| `QuestionSetBenchmarks.ParseBuiltTwenty` | 3.087 us | 568 B | 256 B (unit test, a different three-question set) |
| `QuestionSetBenchmarks.ParseGeneratedTwenty` | 2.457 us | 176 B | — |
| `Answers.Get` | — | — | 0 B (AOT smoke) |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401 with runtime 10.0.12,
with `--job short`, so the means are indicative only.

These are the Phase 2.4 measurements, taken before the neutral question model, so every benchmark figure in this
section is historical. Today a generated and a built set go through the same protocol read, which finds each answer's
key in the definition's UTF-8 keys and fills a slot, and the generated `Create` builds the typed result from the slots.
The two no longer differ in how they scan.

In Phase 2.4 a built set found each answer's question by a linear `ValueTextEquals` scan over its UTF-8 keys, where the
generated parser compiled one `if` chain. At twenty questions the scan cost about 0.63 us more, 3.087 us against
2.457 us, or 26% (1.26 times the generated parse), and allocated 568 B against 176 B mostly for the larger slot array.
That was well inside twice the generated parse, so it did not call for a UTF-8 key map: the scan is short at the sizes a
question set has, and a map, built once at Build, would add a hash per answer for a saving of well under a microsecond.
The scan is still a linear one over the UTF-8 keys, now for both kinds of set.

The budgets come from the AOT smoke app, which measures on its own inputs, not the benchmark's. Today `Build`
measures 2648 B against an unchanged budget of 7296 B, and the built-set round trip measures 3272 B against 3648 B.
Phase 2.4 first budgeted them from 6592 B and 4288 B. The parse budget of 256 B, from a 216 B measurement in Phase 2.4,
is gated in `tests/Minos.NET.Tests` under the JIT, since parsing is internal and the AOT smoke app uses only the public
API.

### Phase 3.1 — Logging

| Benchmark | Mean | Allocated | AOT smoke budget |
|---|---|---|---|
| `ClientBenchmarks.EvaluateAsync` | 2.269 us | 4.17 KB | 5120 B |
| `ClientBenchmarks.EvaluateWithDiscardingLoggerAsync` | 2.402 us | 4.17 KB | 4800 B |
| `ClientBenchmarks.TypedEvaluateAsync` | 1.780 us | 3.29 KB | 4224 B |
| `ClientBenchmarks.TypedEvaluateWithDiscardingLoggerAsync` | 1.960 us | 3.29 KB | 3712 B |
| `ClientBenchmarks.EvaluateYieldingAsync` | 7.994 us | 5.09 KB | — |
| `ClientBenchmarks.EvaluateYieldingWithNullLoggerAsync` | 22.79 us | 5.09 KB | — |
| `ClientBenchmarks.EvaluateYieldingWithDiscardingLoggerAsync` | 12.235 us | 5.56 KB | — |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401 with runtime 10.0.12,
with `--job short`, so the means are indicative only. BenchmarkDotNet prints Allocated in KB (1 KB = 1024 B) to two
decimals, so each figure is good to about 5 B.

Without a logger, or with one whose levels are all disabled, each operation returns the unlogged call itself, so logging
allocates nothing and does only `IsEnabled` checks; with no factory at all, there is no logging decorator either. The AOT smoke gates hold `EvaluateAsync` and `TypedEvaluateAsync` to their
existing budgets with `NullLoggerFactory` and with an every-level-filtered `LoggerFactory`; they measured 4312 B and
3368 B then, inside the unchanged 5120 B and 4224 B (3928 B and 2984 B today, against 4352 B and 3328 B).

The discarding logger is enabled at every level and writes nothing, so every event, timestamp and logging wrapper runs.
- In the benchmark, it adds 0 B to `EvaluateAsync` and 0 B to `TypedEvaluateAsync`, to the precision of the table. The
  mean gaps, 0.13 us and 0.18 us, are within the noise, less than the larger error bar of each pair. The discarding
  logger's per-call `Interlocked` counter, which the AOT gates read, costs a few ns and no allocation, also inside the
  noise.
- Under published win-x64 AOT, the smoke gates measured 4312 B and 3368 B per call with the discarding logger, the same as
  the disabled-logger measurements of 4312 B and 3368 B. Their budgets, 4800 B and 3712 B then, were those measurements plus about 10%, rounded up to the next 64 B.
- The events pass struct state straight to the logger, so what logging adds is the logging wrappers' state machines when a
  call does not complete synchronously.

The canned handler completes synchronously, so the rows above never run those state machines, and neither does the AOT
gate. The `Yielding` rows use a handler that awaits `Task.Yield()` before answering, so the call genuinely completes
asynchronously. There the discarding logger adds about 0.47 KB (5.56 KB against 5.09 KB, roughly 480 B) per call, which is
attributable by elimination: the synchronous rows show the wrapper adds 0 B, so the async delta is the state machines
that only an async completion allocates, and it is the cost a real network call pays. The two yielding means, 7.994 us and
12.235 us, are dominated by the thread pool hand-off and their error bars (11.7 us and 115.8 us) are wider than the
difference, so they show no logging time cost either way; only their allocation figures are reliable.

`EvaluateYieldingWithNullLoggerAsync` runs the same yielding call through `NullLoggerFactory`: it allocates 5.09 KB, the same as the unlogged call, so a disabled logger adds no allocation even where an enabled one adds about 480 B. Its mean comes from a later, noisier run that also re-measured the two rows beside it (26.51 us and 28.30 us, error bars above 200 us), so it says nothing about time. The AOT smoke app makes the same comparison with `GC.GetTotalAllocatedBytes` over 500 awaited calls, the least of three runs: 5254 B with no factory, 5254 B with `NullLoggerFactory` and 5733 B with the discarding logger.

Note, 2026-10-01: since Phase 3.2 that check takes the median of five runs, because yielding runs vary in both directions, and it measured about 5254 B with no factory, 5254 B with `NullLoggerFactory` and 5720-5736 B with the discarding logger.

### Phase 3.2 — Telemetry

| Benchmark | Mean | Allocated | AOT smoke budget |
|---|---|---|---|
| `ClientBenchmarks.EvaluateAsync` | 2.530 us | 4.17 KB | 5120 B |
| `TelemetryBenchmarks.EvaluateListeningAsync` | 3.200 us | 5.51 KB | 6272 B |
| `ClientBenchmarks.TypedEvaluateAsync` | 2.082 us | 3.29 KB | 4224 B |
| `TelemetryBenchmarks.TypedEvaluateListeningAsync` | 6.623 us | 4.81 KB | 5440 B |
| `QuestionSetBenchmarks.EvaluateBuiltSet` | 2.024 us | 3.31 KB | — |
| `TelemetryBenchmarks.EvaluateBuiltSetListeningAsync` | 6.457 us | 4.84 KB | — |
| `ClientBenchmarks.ListModelsAsync` | 1.520 us | 3.08 KB | — |
| `TelemetryBenchmarks.ListModelsListeningAsync` | 1.872 us | 4.09 KB | — |
| `ClientBenchmarks.TypedEvaluateYieldingAsync` | 5.170 us | 4.46 KB | 5056 B |
| `TelemetryBenchmarks.TypedEvaluateYieldingListeningAsync` | 21.724 us | 6.23 KB | — |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401 with runtime 10.0.12,
with `--job short`, so the means are indicative only. BenchmarkDotNet prints Allocated in KB (1 KB = 1024 B) to two
decimals, so each figure is good to about 5 B.
`QuestionSetBenchmarks.EvaluateBuiltSet` and `TelemetryBenchmarks.EvaluateBuiltSetListeningAsync` evaluate the same
three-question triage set, off and listening, so those two rows compare directly. The AOT built-set gates evaluate
`SmokeBuiltSet.Full`, four questions, so they have no row here: `EvaluateBuiltSetRoundTrip` held 4736 B then (3648 B today), and
`EvaluateBuiltSetRoundTripWhileListening` held 5216 B against 5760 B then (4832 B against 5376 B today).

**Telemetry off.** With nothing listening, the generated proxy returns each operation's own task, so it adds nothing.
- The raw evaluation allocates exactly what it did in Phase 3.1: `ClientBenchmarks.EvaluateAsync` matches its Phase 3.1
  row, and the AOT `EvaluateRoundTrip` gate held 5120 B unchanged then (4352 B today). Model listing has no earlier row and no AOT gate; the
  unit gate `OperationsCostTests.NothingListening_ListModels_AddsNothing` shows 0 B through the proxy with nothing listening.
- Typed and built-set calls that complete synchronously allocate exactly what they did in Phase 3.1:
  `ClientBenchmarks.TypedEvaluateAsync` matches its Phase 3.1 row, and under published win-x64 AOT the typed and built-set
  gates measured 3368 B and 3656 B then, inside the unchanged 4224 B and 4736 B (2984 B and 3272 B against 3328 B and 3648 B today).
- A typed or built-set call that completes asynchronously, which every real network call does, pays one state machine
  for the unwrap that hands back the answers and returns the response buffer. The unit test measures it at
  211 B under the JIT, against a 344 B limit, the headroom of the tightest existing gate,
  `TypedEvaluateRoundTripWithDiscardingLogger`.
- Under published win-x64 AOT, an asynchronous `EvaluateAsync<T>` with telemetry off allocated 4568 B per call then (4184 B today), the median
  of five runs, because yielding runs vary in both directions.

Listening minus off, from the table: the raw evaluation 1.34 KB (5.51 against 4.17), the typed call 1.52 KB (4.81 against 3.29),
the built set 1.53 KB (4.84 against 3.31), model listing 1.01 KB (4.09 against 3.08) and the asynchronous typed call 1.77 KB
(6.23 against 4.46). The mean of `TypedEvaluateYieldingListeningAsync`, 21.724 us with an error of 171 us, is dominated by the thread pool
hand-off and noise, so only its allocation figure is reliable.

**Listening.** Discarding listeners sample every span and enable every instrument. Then the call pays for:
- the `Activity`, and the start tags `TagsAtStart` boxes into one `TagList`;
- the boxed tag and measurement values, and a `TagList` per metric;
- the deferred reads.

The reads re-scan the response once per attribute. The only allocation they make is the response model's string. There is no
cache, so the string is built again for each attribute that reads it: the span tag and the duration and token-histogram
metric tags. That cost is included in the figures below; it is measured, not budgeted at zero. Under
published win-x64 AOT the listening gates measured 5680 B, 4928 B and 5216 B per call then (5296 B, 4544 B and 4832 B today).
Their budgets were those measurements plus about 10%, rounded up to the next 64 B.

### Phase 3.3 — DI package

| Benchmark | Mean | Allocated | AOT smoke budget |
|---|---|---|---|
| `ClientBenchmarks.EvaluateAsync` | 18.15 us | 4.17 KB | 5120 B (the existing `EvaluateRoundTrip` budget) |
| `DependencyInjectionBenchmarks.HandBuiltEvaluateAsync` | 5.453 us | 4.23 KB | — |
| `DependencyInjectionBenchmarks.ResolvedEvaluateAsync` | 5.816 us | 4.23 KB | 4864 B |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11, .NET SDK 10.0.401 with runtime 10.0.12, with `--job short`, so
the means are indicative only. BenchmarkDotNet prints Allocated in KB (1 KB = 1024 B) to two decimals, so each figure is
good to about 5 B.

The `ClientBenchmarks.EvaluateAsync` row ran in a separate job on a loaded machine. Its mean, 18.15 us with an error of
70.88 us, is noise and cannot be compared with the DI rows' means. Only its Allocated figure is meaningful.

**DI adds nothing per call.**
- Both `DependencyInjectionBenchmarks` rows make `ClientBenchmarks.EvaluateAsync`'s call.
- The hand-built client runs over an `HttpClient` that `DecisionClient.ConfigureHttpClient` configured, as the factory's is.
  So both send the User-Agent header. That header costs 64 B per call over `ClientBenchmarks.EvaluateAsync`, whose
  borrowed client sends none.
- Under published win-x64 AOT, the gate `EvaluateRoundTripThroughDependencyInjectionAgainstHandBuilt` holds the
  resolved client's total over 1000 calls to the hand-built client's own total from the same run. Since Phase 5.3
  both measure exactly 3992000 B, 3992 B per call. The resolved client also has an absolute budget of 4416 B per
  call: its measurement plus about 10%, rounded up to the next 64 B. Phase 3.3 measured 4376 B per call and set
  that budget at 4864 B; ZeroAlloc.Rest 3.2.1 brought both down.
- Registration and the first resolve happen once and are not budgeted.
- Since Phase 5.3 the gate is ZeroAlloc.TestHelpers' `AllocationGate.AssertNoMoreThanValueTask`, which compares the
  two totals over the gate's iterations, #73. Until then a local helper in the smoke app measured them. Release 1.5.1
  settles the heap before its warm-up, not after: a forced gen2 collection lets `ArrayPool.Shared` drop its arrays
  under high memory load, and measuring their refill made the gate flaky, #79.
- Every existing allocation budget is unchanged.

**The factory's request logging.** `AddHttpClient` gives every named client the factory's logging handlers. These format
the redacted request URI and open a logging scope on every request, before they check whether any logger is enabled.
- With those handlers, the resolved client measured 4720 B against 4376 B per call, under both the JIT and published
  win-x64 AOT. That is 344 B more. This was measured while planning, on 2026-10-01.
- So `AddDecisionClient` removes them with `RemoveAllLoggers()`. The client still logs each operation and each retried attempt
  itself.
- `AddDefaultLogger()` on the `HttpClient` property of the returned builder brings them back, at that cost.

### Phase 3.4 — Options and configuration

Binding from `IConfiguration` and startup validation run once, when the options are first read, so they add nothing per
call.
- Under published win-x64 AOT, the gate `EvaluateRoundTripThroughBoundConfigurationAgainstHandBuilt` holds a client bound
  from configuration to a hand-built client's own total over 1000 calls from the same run. Both measured 4376 B per
  call in Phase 3.4; since Phase 5.3 both measure exactly 3992000 B, 3992 B per call.
- The absolute gate for a DI-resolved client, 4864 B per call then and 4416 B now, and every other existing budget,
  were unchanged by this phase.
- Binding is source-generated. The AOT smoke app binds every option with zero IL2xxx/IL3xxx warnings.

### Phase 4.1 — Pattern helpers

`ConfidenceThresholds.Classify`, `Score<T>.Normalized` and `KeyedScore.Normalized` are arithmetic over the answer
struct. Under published win-x64 AOT, the `PatternHelpers` gate holds all three to 0 B per call on parsed answers, and
every existing budget is unchanged. No benchmark was added: there is no work beyond a few comparisons and a division.

### Phase 5.1 — Public API review

The API review renamed members and parameters and added the `Disposed` kind. It added no benchmark and changed no
budget. Re-measured under published win-x64 AOT on 2026-10-04, three runs, every absolute gate passed:
- A hand-built client over a configured `HttpClient`, and a DI-resolved client, measure 4397 B per call, 21 B more than
  the 4376 B recorded in Phase 3.3. Both sit inside the unchanged 4864 B budget.
- An asynchronous `EvaluateAsync<T>` with telemetry off measures 4587 B and 4589 B, against 4568 B in Phase 3.2 and
  the unchanged 5056 B budget.
- The yielding `EvaluateAsync` comparison measures 5351 to 5363 B with no factory, 5359 to 5363 B with
  `NullLoggerFactory` and 5843 B with the discarding logger, against 5254 B, 5254 B and about 5730 B in Phase 3.2.
- The relative gates against a hand-built client, and the `NullLoggerFactory` comparison, are the known flaky ones
  tracked in #79. In two of the three runs they failed by under 25 B, the same pattern as before.
- Phase 5.3 traced these increases, #68, to the measurement, not the library: see
  [Phase 5.3](#phase-53--the-allocation-creep).

**ZeroAlloc.Rest 3.2.1.** Release 3.2.1 of ZeroAlloc.Rest removes the per-call allocations its generated transport made
even when nothing listened (#95). It adds `Accept` with `TryAddWithoutValidation` instead of a new header value, guards
its metrics on `Enabled` and shares one `TagList`, passes no `params` array, and reads no `Host` without a listener.
Re-measured under published win-x64 AOT on 2026-10-04, three identical runs, every absolute gate passed. The figures are
bytes per call, before and after, with the new budget, which is still the measurement plus about 10%, rounded up to the
next 64 B:

| Gate | Before | After | Old budget | New budget |
|---|---|---|---|---|
| `EvaluateRoundTrip`, and its two logger twins | 4312 B | 3928 B | 5120 B, 4800 B | 4352 B |
| `TypedEvaluateRoundTrip`, and its two logger twins | 3368 B | 2984 B | 4224 B, 3712 B | 3328 B |
| `EvaluateBuiltSetRoundTrip` | 3656 B | 3272 B | 4736 B | 3648 B |
| `EvaluateRoundTripThroughDependencyInjection` | 4376 B | 3992 B | 4864 B | 4416 B |
| `EvaluateRoundTripWhileListening` | 5680 B | 5296 B | 6272 B | 5888 B |
| `TypedEvaluateRoundTripWhileListening` | 4928 B | 4544 B | 5440 B | 5056 B |
| `EvaluateBuiltSetRoundTripWhileListening` | 5216 B | 4832 B | 5760 B | 5376 B |
| `TelemetryOffAsynchronousTypedEvaluation` | 4563 B | 4184 B | 5056 B | 4608 B |

- Every synchronous gate drops by 384 B, so the saving is a fixed cost of the old transport, not a share of the call.
  The listening gates drop by the same 384 B, so the metrics' tag arrays and boxing were paid with nothing listening
  too.
- The hand-built and DI-resolved clients both measure 3992 B, so the relative gates still compare equal.
- The figures in the Phase 3.2 and Phase 3.3 tables and the "unchanged" budgets above describe those phases; the
  budgets in [Native AOT](native-aot.md) are the current ones.
- The unit tests' unwrap headroom is unchanged at 344 B, because the tightest gate,
  `TypedEvaluateRoundTripWithDiscardingLogger`, now has 3328 B over its 2984 B.
- The new budgets were measured on win-x64, and Linux CI holds them too: the `aot-smoke` job passed every gate on
  linux-x64, in [run 37207773054][aot-smoke-run] on commit `bf1ef0b`, with the DI-resolved client at 3992 B there as
  well.
- In the [client comparison](#comparison) below, Minos.NET allocates fewer bytes per call than the hand-written
  `HttpClient` client; its Bytes/call column has the figures.

[aot-smoke-run]: https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/37207773054

### Phase 5.3 — The allocation creep

Phase 5.1 recorded figures above Phase 3's: about 20 B more per call on the synchronous paths, and 5363 B against
5254 B on the yielding call with no logger factory. Phase 5.3 traced this, #68. **No path got more expensive.** The
extra bytes were the measurement's own: a refill of pools that the measuring loop had emptied.

**The cause.** The measuring loops warmed up and then forced gen2 collections. Each gen2 collection runs
`ArrayPool.Shared`'s trim. Once the machine's memory load passes 90% of the runtime's high-load threshold, which this
machine's had by Phase 5.1, the trim drops every pooled array, including the ones the warm-up had just rented. The first
measured calls then rented them again:
- `byte[16384]`, `byte[4096]` and 152 B, 20680 B in all on the raw `EvaluateAsync`, or about 21 B per call over 1000
  calls. Other paths paid 1 to 42 B per call, depending on which pooled arrays they rent, and paths that rent none,
  such as the answer readers and `Build`, paid nothing.
- On the yielding check, the refill was 20680 B over 500 calls, 41.4 B per call. Its 100-call warm-up also left some
  pool threads' own array slots empty, so a window could rent up to two more 16 KB arrays, 33.1 B per call each.
  Together, 5254 + 41.4 + 66.2 = 5362 B, the 5363 B that Phase 5.1 recorded.

The order of the measuring loops is the fix: collect first and wait for the trim to finish, then warm up, then measure.
The library's own helpers have measured that way since Phase 5.3, #79, and the yielding check now warms up for 2000
calls. ZeroAlloc.TestHelpers 1.5.1 fixed `AllocationGate` the same way, TestHelpers#62, and Minos pins it, so every gate
now reads the path's true cost.

**The trace.** The smoke app of each phase merge on `main` since Phase 3.2 was published under win-x64 Native AOT with
the fixed measuring order patched in, three runs each. Every figure was the same in every run:

| Merge | Raw call | DI-resolved | Typed call | Built set | Yielding, no factory | Yielding, telemetry off |
|---|---|---|---|---|---|---|
| Phase 3.2 to Phase 5.1, seven merges | 4312 B | 4376 B | 3368 B | 3656 B | 5250 to 5256 B | 4565 to 4568 B |
| Phase 5.2, ZeroAlloc.Rest 3.2.1 | 3928 B | 3992 B | 2984 B | 3272 B | 4870 to 4872 B | 4181 to 4184 B |

- The DI-resolved column starts at Phase 3.3, which added it.
- The listening gates and the other synchronous gates follow the same pattern: flat through Phase 5.1, then down by
  exactly 384 B at Phase 5.2, or unchanged where the path makes no HTTP call.
- The old measuring order on the Phase 5.1 merge reads 4396.68 B per call on the DI-resolved client, the 4397 B
  Phase 5.1 recorded. The true cost there was 4376 B, Phase 3.3's figure.
- So the only change since Phase 3 is ZeroAlloc.Rest 3.2.1's saving of 384 B per call.

**Today.** Under published win-x64 AOT on 2026-10-07, five runs, through ZeroAlloc.TestHelpers 1.5.1, every
`AllocationGate` gate measures exactly the figure its comment records, with the same total over 1000 calls in every
run. The yielding telemetry-off check measures 4181 to 4185 B against the 4184 B its comment records.

The budgets stay as they are. Each is already the true cost plus about 10%, rounded up to the next 64 B, so no budget
can be tightened under that rule. The old `GeneratedParse` gate, 192 B over 176 B, is now split in two:
`GeneratedCreate` in the smoke app holds the generated `Create` to 128 B over its 112 B measurement, and
`GeneratedSetAllocationTests` in the unit suite holds the protocol's whole read of the answers, buffer included, to the
same 192 B.

### Phase 6.2 — The neutral question model

A set's `questions` JSON is now written by the protocol from its definition on the first call, and cached on the
definition for every later call. That first write is a one-time cost per set, so it is measured here and kept out of
every per-call budget.

| Benchmark | Mean | Allocated |
|---|---:|---:|
| `QuestionSetBenchmarks.NewDefinition` | 167.3 ns | 856 B |
| `QuestionSetBenchmarks.NewDefinitionAndQuestionsJson` | 953.8 ns | 6088 B |

Both build a new definition over the three triage questions of `QuestionSetBenchmarks.Build`; the second then writes its
`questions` object. The difference, about 0.8 us and 5232 B, is the one-time cost of a set's first serialization: the
growing buffer, the JSON writer and the finished byte array. Measured on 2026-10-10 on the machine in
[Comparison](#comparison), with `--job short`, so the means are indicative only.

### Phase 6.3 — The client pipeline

Every `DecisionRequest` call, and so every typed and built-set call, now runs through the
[standard pipeline](pipeline.md): telemetry, logging, retries and a transport. The phase added six AOT gates. The
figures are bytes per call under published win-x64 Native AOT, on ZeroAlloc.Rest 3.3.0, and each budget is the
measurement plus about 10%, rounded up to the next 64 B, per the Phase 1.8 rule.

| Gate | What it measures | Measured | Budget |
|---|---|---|---|
| `NeutralEvaluateRoundTrip` | `EvaluateAsync(DecisionRequest)` on the standard pipeline, nothing listening | 3248 B | 3584 B |
| `NeutralEvaluateRoundTripWhileListening` | The same call with a span and metric listener attached | 4616 B | 5120 B |
| `BareTransportRoundTrip` | The same call with `UseStandardPipeline` off, the transport alone | 3248 B | 3584 B |
| `PassThroughStage` | A `DelegatingDecisionClient` that overrides nothing, over an inner call that completes synchronously | 0 B | 0 B |
| `Utf8StateEvaluateRoundTrip` | A typed `EvaluateUtf8Async<T>` call | 3424 B | 4096 B |
| `TypedStateEvaluateRoundTrip` | A typed `EvaluateAsync<T, TState>` call, over a one-question response | 3016 B | 3328 B |

What each one allocates:

- **The neutral call** allocates the request's `Utf8JsonWriter` and `RawJson`, ZeroAlloc.Rest's per-attempt
  `HttpRequestMessage`, headers, `MemoryStream` and `StreamContent`, the response's `HttpResponseMessage` and body
  buffering, and the `DecisionResponse` with its `AnswerSlot[]` and probabilities.
- **The standard stages add nothing** to a call that completes synchronously with nothing listening, so the standard
  pipeline and the bare transport measure the same 3248 B.
- **A stage that overrides nothing costs nothing.** `DelegatingDecisionClient` returns the inner client's completed
  `ValueTask` as it is, so `PassThroughStage` is budgeted at exactly 0 B.
- **Listening** adds the `Activity`, its boxed start tags, the boxed tag and measurement values and each metric's
  `TagList`: 1368 B over the idle call. A typed call pays the same 1368 B, 4792 B against 3424 B.
- **The UTF-8 state** costs nothing: the request keeps the caller's array and the request writer copies its bytes, so
  the call measures `TypedEvaluateRoundTrip`'s 3424 B. Its 4096 B budget was set over the 3704 B it measured while it
  parsed the state into a 280 B `JsonDocument`.
- **The typed state** costs the transport's request and response, the `DecisionResponse`, the result record, and the
  serialized state: the `Utf8JsonWriter` and pooled `RawJson` it is written through, and the array of its bytes the
  request keeps. It measured 3008 B while the state was a `JsonDocument` from `DecisionContent.FromValue`, which
  rewrote a converter's raw JSON.
- **The `JsonElement` state** costs one clone of the element per call, so you may dispose its document as soon as the
  call returns: 304 B for a one-message chat log in the unit suite. No AOT gate covers this overload. The text overload
  adds nothing for its state.

The existing gates over the typed and built-set paths now include the `DecisionRequest`, the `DecisionResponse` and its
`AnswerSlot[]`. A typed call measures 3424 B, against 2984 B before the pipeline, and a built-set call 3616 B, against
3272 B. Listening, they measure 4792 B and 4984 B, against 4544 B and 4832 B. A typed call that completes
asynchronously with telemetry off measures 4565 B, against 4184 B, which includes every boxed async state machine,
among them the retry stage's and the typed extension's. No budget was raised.

**ZeroAlloc.Rest 3.3.0's path escaping.** The transport posts to the protocol's endpoint path through a `{**path}`
route, which ZeroAlloc.Rest 3.3.0 added in
[ZeroAlloc.Rest#422](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/422). Release 3.3.0 escapes that path on
every call with a `StringBuilder` and a string, about 176 B, and every HTTP figure on this page from Phase 6.3
includes it. [ZeroAlloc.Rest#425](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/425)
removes it in release 3.3.1. A build with a constant route, which stands in for 3.3.1, measures 3072 B for the neutral
call and the bare transport, 4440 B listening, 3248 B for the UTF-8 state and 2840 B for the typed state, and 3248 B
for the typed call. On 3.3.0 the typed call's 3424 B is above its 3328 B budget, so `TypedEvaluateRoundTrip` and its
two logger twins fail until Minos adopts 3.3.1. Every gate will be measured again then, and the new budgets set from
those figures.

## Comparison

Minos.NET against six other clients, all calling one local mock with the same request:
- `raw-httpclient`, the client a developer would write by hand with `HttpClient` and System.Text.Json. Like every
  other client, it builds and serializes the request on every call, here with a source-generated serializer context;
- the community .NET clients JevSharp, TypeSafe.AI.Sdk and Jev.Net, the three most downloaded of eleven on NuGet;
- TypeSafe's official JS SDK, `@typesafe-ai/sdk`, and its official Python SDK, `typesafe-sdk`.

Every client asks the same two questions and parses the same recorded answer, described in [the workload][workload].
Each makes one attempt per call, with retries off.

Each library had to pass three checks to be included: a licence that allows its use, a custom base URL and a
single-attempt setting. A library that failed would have been left out, not patched. All five passed, so none was left
out. The checks and their sources are in [the library-check document][library-checks].

### What the numbers measure

- **The client's own cost per call.** Every call goes over loopback to a minimal Kestrel mock, which serves one cached
  response and keeps no log. So the figures hold no network latency and no server work. Latency does include the mock's
  minimal per-request cost, which is the same for every client.
- **Steady state.** Each client is measured warm, in a warm process. The figures leave out first-call cost: building the
  client and the JIT compilation of its code, generated code included.
- **The same warm-up for every client.** Before its latency loop, every client runs 16 concurrent workers for 2 s on
  the instance that is then measured, then makes 200 warm-up calls one at a time. The .NET, JS and Python harnesses
  all do this.
- **Latency:** after the warm-up, 2000 calls one at a time, each timed on its own. The mean is the arithmetic mean of
  the call times; p50 and p99 are by nearest rank. The five .NET clients share one process, so their calls are
  interleaved: 20 rounds in which every client makes 100 timed calls, in an order that rotates each round, with each
  client's times pooled over its rounds.
- **Throughput:** 16 concurrent workers for 10 s, after a 2 s warm-up, one client at a time. The .NET harness rotates
  its clients' order from run to run and records it. The mock counts every request, and a run fails unless each call
  sent exactly one.
- **Client instances.** Each .NET and JS client uses one long-lived instance. The Python SDK splits sync and async
  calls between two classes, so the Python harness uses one long-lived synchronous `TypeSafeClient` for the warm-up and
  the latency loop, and one long-lived `AsyncTypeSafeClient` for throughput.
- **Transports.** All five .NET clients run on one `SocketsHttpHandler` configuration, each with its own instance:
  pooled connections that live 2 minutes, automatic decompression off and the default connection limit. The JS and
  Python SDKs use their own default transports: Node's built-in `fetch`, and the Python SDK's own HTTP client.
- **Of mock ceiling:** the client's throughput as a share of the mock ceiling. The ceiling is the highest rate the raw
  client reached against the mock, the best of 16, 32 and 64 workers, in the same run and on the same cores. So it is a
  lower bound on what the mock can serve: the run doesn't show whether the mock or the raw client was the limit.
- **Separate physical cores.** The mock runs on one set of cores and the clients on the other; the machine line names
  both. The split keeps the SMT sibling threads of each physical core on one side, so the mock never runs on a core the
  clients run on. The two sides still share the last-level cache and the memory bandwidth, so the mock's work can still
  slow the client being measured a little.
- **Bytes/call:** BenchmarkDotNet's MemoryDiagnoser, for the .NET clients only. Compare bytes only between the .NET
  clients; Node and Python have no equivalent figure, so their rows show a dash.
- **One run at a time.** Compare clients only within one run. Shared CI runners differ in CPU and load from one run to
  the next, so the absolute figures move between runs; see [Across runs](#across-runs).

<!-- comparison: benchmarks/compare/results/ci-run-1.json -->
### ZeroAlloc.Jev: client comparison

| Client | Library | Runtime | Mean (ms) | p50 (ms) | p99 (ms) | Throughput (/s) | Of mock ceiling | Bytes/call |
|---|---|---|--:|--:|--:|--:|--:|--:|
| **zeroalloc-jev** | ZeroAlloc.Jev 0.4.0-local+db54c90 | .NET 10.0.12 | 0.052 | 0.048 | 0.094 | 65,672 | 99% | 5,416 |
| raw-httpclient | HttpClient, System.Text.Json 10.0.12 | .NET 10.0.12 | 0.058 | 0.054 | 0.109 | 65,029 | 98% | 6,456 |
| jev-net | Jev.Net 0.4.0 | .NET 10.0.12 | 0.065 | 0.063 | 0.117 | 44,848 | 68% | 20,232 |
| typesafe-ai-sdk | TypeSafe.AI.Sdk 0.3.0 | .NET 10.0.12 | 0.078 | 0.074 | 0.139 | 40,114 | 61% | 26,673 |
| jevsharp[^1] | JevSharp 0.2.0 | .NET 10.0.12 | 0.168 | 0.170 | 0.245 | 27,415 | 41% | 51,913 |
| typesafe-ai-sdk-js | @typesafe-ai/sdk 0.6.0 | Node.js 24.21.0 | 0.368 | 0.284 | 0.923 | 5,793 | 9% | — |
| typesafe-sdk-python | typesafe-sdk 0.7.2 | Python 3.12.14 | 0.584 | 0.581 | 0.699 | 1,221 | 2% | — |

Machine: ci; OS: Ubuntu 24.04.5 LTS; CPU: INTEL(R) XEON(R) PLATINUM 8573C; date: 2026-10-04T15:34:07Z; mock cores: 0-1; client cores: 2-3; mock ceiling: 66,103/s.

Order: latency in 20 interleaved rounds of 100 calls per .NET client, rotated by round from rotation 4; throughput jev-net, zeroalloc-jev, raw-httpclient, jevsharp, typesafe-ai-sdk.

Run: [run 37213251867](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37213251867), commit `fcc4cee`.

[^1]: JevSharp reaches a custom endpoint with static headers only, so it sends no auth header.
<!-- endComparison -->

The table is `merge.py`'s output for [the first of the published runs][ci-json], which holds the run's result files
whole, and a docs test fails if the two differ. The run used a GitHub-hosted `ubuntu-latest` runner; the machine line
gives its CPU and the cores the mock and the clients ran on. The three published runs were measured on 2026-10-04,
before the library was renamed, so their result files call it ZeroAlloc.Jev; the code measured is the same.

In this run and in both others under [Across runs](#across-runs), Minos.NET had the lowest mean latency, the
highest throughput and the fewest bytes per call of any client. Its throughput lead over the raw client is small; the
other clients stay well behind both, and their throughput order was the same in every run: Jev.Net, TypeSafe.AI.Sdk,
JevSharp, the JS SDK and the Python SDK.

In every run the raw client and Minos.NET come close to the mock ceiling, and they can pass it: the ceiling is
only a lower bound on what the mock can serve, the best rate the raw client reached in its own, separate measurement.
The Of mock ceiling column in each table shows how close each run came. Near the ceiling, the mock's own speed may
narrow the gap between those two. The other clients stay well below it in every run, so their figures are their own.

**Minos.NET is built from the branch.** Its Library cell shows the version a build that is not a release gets, the
last release with a `-local` suffix, followed by the commit CI built, as in `<release>-local+<commit>`. So the tables
measure branch commit `fcc4cee`, not a published release. A pull-request run builds GitHub's merge of the branch into
`main`, so the commit in the version is that merge commit; the run line names the branch commit it came from.

**JevSharp sends no auth header.** It reaches a custom endpoint with static headers only, and a custom endpoint is its
only way to reach the mock. So each of its calls does a little less work than the other clients' calls.

**The JS and Python throughput is one thread's.** Each SDK is bound by one CPU-bound thread: the Node event loop, and
one Python interpreter thread. Their throughput is that thread's ceiling, and more workers don't raise it. The evidence
is in the [JS harness's README][js-readme] and the [Python harness's README][py-readme].

**Versions.** The Library and Runtime columns give the version of every library and runtime the run measured. The
community .NET clients come from NuGet, and the raw client uses the runtime's own System.Text.Json. The Python SDK's
dependencies are pinned in [`requirements.txt`][py-requirements], and the JS SDK's in its `package-lock.json`.

A run on an idle local machine, which is steadier than a shared runner, is still to come: see [issue #97][issue-97].

### Across runs

Shared runners differ from run to run, so the comparison ran on CI three times on the same commit, each time on a
fresh runner. The first table gives each run, with its CPU and mock ceiling. The second gives each client's lowest and
highest figure over those runs. It shows how far each figure moved between runners, and it is no ranking: compare
clients within one run. The table above is the first run; the result files of all three are in
[`benchmarks/compare/results`][results]. Within each run the .NET clients' latency calls are interleaved and their
throughput order rotates, as [What the numbers measure](#what-the-numbers-measure) describes, so no client gains from
its place in the order.

<!-- acrossRuns: benchmarks/compare/results/ci-run-1.json benchmarks/compare/results/ci-run-2.json benchmarks/compare/results/ci-run-3.json -->
### ZeroAlloc.Jev: across runs

| Run | Commit | CPU | Mock ceiling (/s) |
|---|---|---|--:|
| [run 37213251867](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37213251867) | `fcc4cee` | INTEL(R) XEON(R) PLATINUM 8573C | 66,103 |
| [run 37213281420](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37213281420) | `fcc4cee` | AMD EPYC 7763 64-Core Processor | 37,751 |
| [run 37213313491](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37213313491) | `fcc4cee` | Intel(R) Xeon(R) Platinum 8370C CPU @ 2.80GHz | 52,157 |

| Client | Mean (ms) | Throughput (/s) | Of mock ceiling | Bytes/call |
|---|--:|--:|--:|--:|
| **zeroalloc-jev** | 0.052 to 0.108 | 34,748 to 65,672 | 92% to 108% | 5,416 |
| raw-httpclient | 0.058 to 0.132 | 34,690 to 65,029 | 92% to 104% | 6,456 |
| jev-net | 0.065 to 0.140 | 25,147 to 44,848 | 67% to 72% | 20,232 |
| typesafe-ai-sdk | 0.078 to 0.170 | 23,223 to 40,114 | 61% to 63% | 26,656 to 26,673 |
| jevsharp | 0.168 to 0.233 | 16,999 to 27,415 | 41% to 45% | 51,882 to 52,069 |
| typesafe-ai-sdk-js | 0.368 to 0.479 | 3,418 to 5,793 | 8% to 9% | — |
| typesafe-sdk-python | 0.584 to 0.742 | 1,022 to 1,221 | 2% to 3% | — |

Runs: 3. Each range is the lowest to the highest figure over the runs.
<!-- endAcrossRuns -->

Both tables are `merge.py --across`'s output for the published runs the marker above names, and a docs test fails if
they differ.

### Reproducing the run

- **Locally:** `benchmarks/compare/run.ps1`, or `run.sh` on Linux, builds everything, runs the mock and the three
  harnesses and merges the results. Keep the machine idle. See [its README][compare-readme].
- **On CI:** add the `benchmarks:compare` label to a pull request that changes the comparison's files or the library,
  or start the **Benchmarks: compare** workflow by `workflow_dispatch`. The `full` job uploads the result files and the
  table as the `compare-full` artifact.
- **Publishing a run:** `merge.py` with `--run-url`, `--commit` and `--save` writes the result files as one published
  run, and prints the table that goes between the comparison markers on this page. `merge.py --across` with the
  published runs prints the tables that go between the across-runs markers.

[workload]: https://github.com/MarcelRoozekrans/Minos.NET/blob/main/benchmarks/compare/workload/README.md
[library-checks]: https://github.com/MarcelRoozekrans/Minos.NET/blob/main/docs/plans/2026-10-04-phase-5.2-library-checks.md
[ci-json]: https://github.com/MarcelRoozekrans/Minos.NET/blob/main/benchmarks/compare/results/ci-run-1.json
[results]: https://github.com/MarcelRoozekrans/Minos.NET/tree/main/benchmarks/compare/results
[js-readme]: https://github.com/MarcelRoozekrans/Minos.NET/blob/main/benchmarks/compare-js/README.md#why-the-js-throughput-is-lower
[py-readme]: https://github.com/MarcelRoozekrans/Minos.NET/blob/main/benchmarks/compare-py/README.md#why-the-python-throughput-is-lower
[py-requirements]: https://github.com/MarcelRoozekrans/Minos.NET/blob/main/benchmarks/compare-py/requirements.txt
[issue-97]: https://github.com/MarcelRoozekrans/Minos.NET/issues/97
[compare-readme]: https://github.com/MarcelRoozekrans/Minos.NET/blob/main/benchmarks/compare/README.md

## Next

- [Getting started](getting-started.md): the guide from the beginning.
- [Testing your code](testing-your-code.md): test code that uses Minos with canned answers.
- [Samples](samples.md): three runnable cookbook samples.
