---
id: native-aot
title: Native AOT and allocations
sidebar_position: 9
description: What it means that the Minos client is Native AOT compatible, the one reflection it uses, how CI checks every public member, and the allocation budgets that guard it.
---

# Native AOT and allocations

Native AOT compiles a .NET program ahead of time into a native executable. There is no just-in-time compiler at run
time, and the compiler removes code that it cannot see being used, which is called trimming. The result starts fast and
is small, but a library has to cooperate. Code that finds types or members by name at run time, which is reflection,
can lose the very things it looks for.

`Minos.NET` is written for this. This page says what that covers, where it is checked, and what the client
allocates, because a program that avoids the just-in-time compiler usually cares about memory as well.

## What is Native AOT compatible

- **The client.** `Minos.NET` sets `IsAotCompatible`, which turns on the trimming and Native AOT analyzers for the
  library's own build. Sending requests, mapping every failure to a `DecisionError`, retries, logging and telemetry all run
  under Native AOT.
- **The generated question sets.** The `[Questions]` generator writes the question JSON and the answer parsing as
  ordinary C# at compile time. The set uses no reflection at run time, and neither does reading an answer.
- **Dependency injection.** `Minos.NET.DependencyInjection` is Native AOT compatible too. It binds options from
  configuration with the configuration binding source generator, so binding uses no reflection and needs nothing set up
  in your program. [Dependency injection](dependency-injection.md#options-from-configuration) covers the options.

## The one use of reflection

A question set [built at run time](question-sets-at-run-time.md) can take a Choice or a Score from an enum. To read the
enum's members, the builder reads its public fields. This is the only reflection in the library, and it is declared to
the trimmer with `DynamicallyAccessedMembers`, so the trimmer keeps those fields and the code is safe under trimming and
Native AOT. You do not need to do anything for it.

There is one case that does ask something of you. A method of your own that passes its own generic parameter on to
`Choice<T>`, `Score<T>` or `Answers.Get` needs the same annotation on that parameter,
`[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]`.
[Question sets at run time](question-sets-at-run-time.md#build-once-and-share) says where.

## State types need JSON metadata

A typed [state](typed-evaluation.md#the-state), such as a support ticket record, is written as JSON through
`System.Text.Json`. Under Native AOT that needs source-generated metadata, which a `JsonSerializerContext` gives. Pass
the context's `JsonTypeInfo` to the `EvaluateAsync<T, TState>` overload, and nothing is found by reflection. A state
you pass as a string, a `JsonElement` or UTF-8 JSON needs no metadata at all, and `DecisionContent.FromValue` takes the same
`JsonTypeInfo`.

## Publishing your own application

Add `PublishAot` to the project that you publish, and publish for a runtime identifier:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

```shell
dotnet publish -c Release -r linux-x64
```

Publishing for Native AOT needs the native toolchain of the platform. Microsoft's
[Native AOT deployment guide](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) lists it. A publish that
reports trimming or AOT warnings, whose codes start with `IL2` or `IL3`, is telling you that something in the program
will not survive, and the warning names it.

## Where it is checked

Three checks guard it, and each answers a different question.

### The smoke application: does it run

The repository holds a smoke application, `samples/Minos.NET.AotSmoke`. The `aot-smoke` job of the CI workflow
publishes it with `PublishAot` and runs the native executable. The project treats every warning as an error, including
the whole `IL2xxx` and `IL3xxx` range, so a trimming or AOT warning anywhere in what the application uses fails the
build. The program exits with a failure if any check fails.

The checks run the real client over a canned HTTP handler. They cover:

- raw, typed and built-set evaluation, and a typed state;
- failures: a rejected request, an unreadable response, an overloaded service that is retried, and the unsupported
  model listing on OpenRouter;
- logging through a real `LoggerFactory`, and the telemetry spans and metrics through real listeners;
- registering clients with [dependency injection](dependency-injection.md), and binding their options from
  configuration;
- the allocation budgets in the next section.

### The whole-assembly check: is anything unsafe

A publish only analyses what the program reaches, so the smoke application cannot vouch for a member that no check
calls. The `aot-surface` job closes that gap. It publishes `samples/Minos.NET.AotSurface` with `PublishAot` and
full trimming, and roots `Minos.NET` and `Minos.NET.DependencyInjection` whole through `TrimmerRootAssembly`.

Rooting analyses every non-generic member of both packages, called or not, and every generic member the compiler can
share across reference types. A value-type generic has no such shared form, so rooting alone skips it. That covers
`Choice<T>`, `Score<T>` and the other generics over an enum, whose type parameter is `where T : struct, Enum`. The host
therefore instantiates every public generic type and generic method of both packages over its own types, and the
compiler analyses them through those instantiations. A test in `tests/Minos.NET.AotSmoke.Tests` fails when a public
generic is not instantiated in the host. The host is rooted as well. Its `[Questions]` sets, one with a Noul, a
Choice and a Score and one with a typed state, mean the code the generator writes is analysed in full too.

Every `IL2xxx` and `IL3xxx` warning is an error, so a trimming or AOT hazard in the public API fails the job. The host
is only published and never run, because the publish is the check. A hazard that nothing calls fails the build all the
same. That was confirmed by looking a type up by name in a never-called method, and again inside a generic method and
a member of a generic type over an enum.

### The coverage rule: does the smoke application leave anything out

The smoke application has to call everything a user can call, or its own pass says too little. The test project
`tests/Minos.NET.AotSmoke.Tests` enforces that, and it runs with the rest of the test suite.

An **entry point** is a public or protected method or constructor that you call. The list is read from the four
`PublicAPI` files, so a new public member is an entry point the day it is added. These are not entry points, and the
test says why for each:

- type declarations, which are called through their constructors and methods;
- property and indexer accessors, fields, constants and enum members, which are covered through their types;
- operators, including implicit conversions, which are syntax over methods that count;
- the `Equals(object)`, `GetHashCode()` and `ToString()` overrides;
- the members the compiler writes for a record, such as `<Clone>$`, `Deconstruct`, `PrintMembers` and `Equals(T)`.
  The test recognises them by the `[CompilerGenerated]` attribute the compiler puts on them, not by name, so a record
  member written by hand, and the `Equals(T)` of a struct, do count.

Every other entry point has to be declared. A smoke check carries one `[Covers]` attribute per entry point it calls,
spelled as its line in the `PublicAPI` file. The test fails in four cases:

- an entry point that no check declares;
- a declaration that names no entry point, such as a stale line or a typo;
- a check that declares something its code never calls. The test binds the check's code with the compiler's semantic
  model, follows it into the smoke application's own helpers, and compares the symbols it finds;
- a declaring check that `Main` does not run.

A **default interface method** is an entry point too, and the rule for it is stricter. Calling it through a client
resolved from a container proves nothing, because that client may override it and the default body would never run. A
check counts for a default interface method only when the receiver is known from the code to be a type that does not
override it: either its own type, or the type its local was created as, when nothing assigns that local again. Since
0.8, `IDecisionClient` has no default methods, because its typed calls became extension methods, so the rule applies
to the next interface that gets one.

A **protected override** of a library member, such as a stage's `Dispose(bool)`, counts as called when a check creates
the smoke type that declares it. Only the library's own code can call such a member, as `DelegatingDecisionClient`'s
`Dispose()` calls `Dispose(bool)`.

The rule is about reach, not about correctness: it shows that the code binds to the entry point and that `Main` runs the
check, not that the call asserts the right thing.

## What the client allocates

An allocation is memory the garbage collector must later reclaim. A client that allocates little causes few collections,
which keeps the pauses short in a program that makes many calls. Minos is built to allocate little. The typed and
built-set calls write the request into a pooled buffer and read the response from one, and reading an answer creates no
object.

- **Reading an answer allocates nothing.** `Noul`, `Choice<T>` and `Score<T>` are read as structs, and so are the
  `Probabilities`, the confidence helpers and `Answers.Get` for a [set built at run
  time](question-sets-at-run-time.md).
- **Parsing a typed answer set allocates the result.** That is the result record, plus the shared buffer that holds the
  probabilities of its answers, and nothing else.
- **A whole call allocates a few kilobytes.** Over the canned handler, a typed call measures 2984 B under Native AOT,
  and [Performance](performance.md) has the measurements for the other paths.
- **Building a question set allocates the set.** It measures 2648 B, so build it once and share it, as the
  [run-time page](question-sets-at-run-time.md) advises.
- **Logging and telemetry add nothing until something listens.** With no logger, or every level off, a call
  allocates nothing extra. With nothing listening to the source or the meter, telemetry adds nothing either. Before
  the [pipeline](pipeline.md), it added 211 B, measured under the JIT, to a typed or built-set call that completed
  asynchronously. [Logging, traces and metrics](observability.md#the-cost-of-logging) says what each adds when it is on.

These claims are enforced, not only measured. The smoke application runs each path below repeatedly, mostly under
`AllocationGate` from the ZeroAlloc.TestHelpers package, and fails if the path allocates more than its budget. The calls
run over a canned in-memory handler, so the budgets measure the client's own work and not the network.

### The allocation budgets

A budget is set a little above what the path measures under Native AOT, usually the measurement plus about 10 percent,
rounded up to the next 64 bytes. Where the measurement is zero the budget is zero, and the first gates have looser
budgets that [Performance](performance.md) notes. A gate that fails fails the `aot-smoke` job, so an allocation
regression cannot reach a release unnoticed.

| Gate | What it measures | Budget in bytes |
| --- | --- | --- |
| `AnswersGet` | Reading answers of a built set through its handles. | 0 |
| `PatternHelpers` | The confidence and normalization helpers of the patterns. | 0 |
| `NoulEquals` | Comparing two `Noul` answers, directly and through `EqualityComparer<Noul>.Default`. | 0 |
| `AnswerSlotAccessors` | Reading a `Noul`, a `Choice<T>` and a `Score<T>` from the answer slots of a typed set. | 0 |
| `GeneratedCreate` | The generated `Create` of a typed set building its result from the answer slots, which replaces the old `GeneratedParse` gate of 192. The protocol's probability buffer is counted by the typed round trips and by a test in the unit suite, which holds the whole read to 192. | 128 |
| `EvaluateRoundTrip` | A raw `EvaluateAsync` call. | 4352 |
| `TypedEvaluateRoundTrip` | A typed `EvaluateAsync<T>` call. | 3328 |
| `NeutralEvaluateRoundTrip` | A neutral `EvaluateAsync(DecisionRequest)` call on the standard pipeline. | 3584 |
| `BareTransportRoundTrip` | A neutral call with `UseStandardPipeline` off, the transport alone. | 3584 |
| `PassThroughStage` | A `DelegatingDecisionClient` that overrides nothing, over an inner call that completes synchronously. | 0 |
| `Utf8StateEvaluateRoundTrip` | A typed `EvaluateUtf8Async<T>` call, which parses its state into a `JsonDocument`. | 4096 |
| `TypedStateEvaluateRoundTrip` | A typed `EvaluateAsync<T, TState>` call, which serializes its state into a `JsonDocument`. | 3328 |
| `EvaluateBuiltSetRoundTrip` | An `EvaluateAsync` call over a built set. | 3648 |
| `BuildQuestionSet` | Building a question set. | 7296 |
| `ContentFromValue` | `DecisionContent.FromValue`. | 320 |
| `ContentFromUtf8Json` | `DecisionContent.FromUtf8Json`. | 320 |
| `EvaluateRoundTripWithNullLoggerFactory` | A raw call with a logger factory that logs nothing. | 4352 |
| `TypedEvaluateRoundTripWithEveryLevelFiltered` | A typed call with every log level filtered out. | 3328 |
| `EvaluateRoundTripWithDiscardingLogger` | A raw call with every log level on. | 4352 |
| `TypedEvaluateRoundTripWithDiscardingLogger` | A typed call with every log level on. | 3328 |
| `EvaluateRoundTripThroughDependencyInjection` | A raw call through a client resolved from the container, and no more than a hand-built client's own measurement. | 4416 |
| `EvaluateRoundTripWhileListening` | A raw call with a span and metric listener attached. | 5888 |
| `TypedEvaluateRoundTripWhileListening` | A typed call with the listeners attached. | 5056 |
| `NeutralEvaluateRoundTripWhileListening` | A neutral call with the listeners attached. | 5120 |
| `EvaluateBuiltSetRoundTripWhileListening` | A built-set call with the listeners attached. | 5376 |
| `EvaluateRoundTripThroughBoundConfiguration` | A raw call through a client bound from configuration, equal to a hand-built client's own measurement. | same as the hand-built client |
| `DisabledLoggerAddsNothingWhereAnEnabledOneDoes` | Asynchronous calls with no factory, a null factory and an enabled logger. Checks the disabled ones add no more than 16 B per call, a tolerance for the noise of a process-wide counter ([#104](https://github.com/MarcelRoozekrans/Minos.NET/issues/104)). | no byte budget |
| `TelemetryOffAsynchronousTypedEvaluation` | A typed call that completes asynchronously, with nothing listening. The median of five runs. | 4608 |

The two logging rows with a logger that does nothing keep the budgets of the calls without one, because the client takes
the unlogged path. The two rows with every level on are higher, and the two listening rows show what a span and the
metrics cost. Each budget has a section in [Performance](performance.md) that explains the measurement behind it. The
phases there are [3.1 for logging](performance.md#phase-31--logging), [3.2 for
telemetry](performance.md#phase-32--telemetry), [3.3 for dependency injection](performance.md#phase-33--di-package) and
[6.3 for the client pipeline](performance.md#phase-63--the-client-pipeline).

A call that completes asynchronously, as every real network call does, pays for a state machine that a synchronous call
does not. The canned handler completes synchronously, so most gates do not see that cost, and the last gate in the table
exists to measure it.

## Next

- [Diagnostics](diagnostics.md): the compile-time rules for question sets.
- [Logging, traces and metrics](observability.md): what the client reports about each call.
- [Performance](performance.md): the benchmarks and the measurements behind every budget.
