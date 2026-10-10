---
id: observability
title: Logging, traces and metrics
sidebar_position: 7
description: What the Minos client logs, which spans and metrics it emits, what it never records, and what listening costs.
---

# Logging, traces and metrics

A client that calls a remote model needs to be observable: you want to know how long each call took, which ones failed
and why, how many tokens you used, and how sure Jev was. `DecisionClient` reports all of this through the three signals that
.NET has built in:

- **Logs**, through `Microsoft.Extensions.Logging`, when you give the client an `ILoggerFactory`.
- **Traces**, as `System.Diagnostics` spans, from an `ActivitySource` named `Minos`.
- **Metrics**, as `System.Diagnostics.Metrics` instruments, from a `Meter` named `Minos`.

All three describe the call and never its content. Your state, your questions, the answers, your API key and the
server's error text stay out of every one of them, and the sections below say exactly where the line is drawn. All
three are also cheap: with nothing listening they add nothing to calls that complete synchronously, and a small fixed
cost to asynchronous ones. [The cost of listening](#the-cost-of-listening) and
[the cost of logging](#the-cost-of-logging) give the figures.

## Logging

Pass an `ILoggerFactory` when you create the client, as [the client page](client-and-errors.md#creating-a-client) shows.
Without one, or with `null`, the client logs nothing. A client that [dependency injection](dependency-injection.md)
registers logs through the container's `ILoggerFactory`, so there is nothing to pass.

The client logs in the category `Minos.DecisionClient`. This example makes one evaluation and returns what was
logged. `FakeLoggerProvider`, from `Microsoft.Extensions.Diagnostics.Testing`, keeps the records in memory. An
application would add a real provider, such as the console, instead.

```shell
dotnet add package Microsoft.Extensions.Diagnostics.Testing
```

<!-- snippet: Observability_Logging -->
```cs
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Minos;

// One question, so that the events are easy to read.
[Questions]
public partial record ObservedUrgency
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

public static class ObservedLogging
{
    // A client logs through the ILoggerFactory it is given. FakeLoggerProvider keeps every record in memory, which
    // is what a test wants. An application would add its own provider instead, such as the console or Serilog.
    public static async Task<IReadOnlyList<FakeLogRecord>> EvaluateAndCollectAsync(
        HttpClient http, DecisionClientOptions options, CancellationToken cancellationToken)
    {
        using var provider = new FakeLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder
            .AddProvider(provider)
            .SetMinimumLevel(LogLevel.Debug));
        using var client = new DecisionClient(http, options, loggers);

        // The result is not inspected here: each outcome, a success or a failure, is one log event.
        await client.EvaluateAsync<ObservedUrgency>("Help! The server is down.", cancellationToken);

        return provider.Collector.GetSnapshot();
    }
}
```
<!-- endSnippet -->

Each record has an event id, a level, a message and the structured fields behind it. A provider that keeps structure,
such as a JSON console or Serilog, receives each `{Placeholder}` below as a field of that name.

### The events

| Id | Event | Level | Message template |
| --- | --- | --- | --- |
| 1001 | `EvaluationSucceeded` | Debug | `Minos {Operation} on {Model} via {Provider} succeeded: {QuestionCount} questions in {DurationMs} ms.` |
| 1002 | `EvaluationFailed` | Warning | `Minos {Operation} on {Model} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}` |
| 1003 | `AttemptRetrying` | Warning | `Minos attempt {Attempt} failed with {ErrorKind}, status {StatusCode}, retry-after {RetryAfter}; retrying.` |
| 1004 | `ModelsListed` | Debug | `Minos list-models via {Provider} succeeded: {ModelCount} models in {DurationMs} ms.` |
| 1005 | `ModelsListFailed` | Warning | `Minos list-models via {Provider} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}` |
| 1006 | `UnexpectedException` | Error | `Minos {Operation} threw an unexpected exception.` The exception is attached to the record. |

A call logs once, when it completes, and logs again for each attempt it is about to retry.

- **`Operation`** names the call: `evaluate` for the raw `EvaluateAsync(SystemOneRequest)`, `evaluate-set` for every
  `EvaluateAsync(DecisionRequest)` call, the typed overloads and a [built question set](question-sets-at-run-time.md)
  included, and `list-models` for model listing. An `evaluate-set` event logs `Provider` as the client's telemetry
  name, such as `typesafe`; the raw calls log it as `TypeSafe` or `OpenRouter`.
- **Success** is a Debug event, 1001 for an evaluation and 1004 for a model listing. `QuestionCount` is the number of
  questions asked.
- **Failure** is a Warning, 1002 or 1005. It covers every failure the call returns as a
  [`DecisionError`](client-and-errors.md#errors), after the retries are used up, including an `InvalidResponse` from reading
  typed answers. `ErrorKind` is the `DecisionErrorKind`. `StatusCode` is the HTTP status, or null when no response arrived,
  and the message then reads `status (null)`.
- **A retry** is a Warning, 1003, logged for each failed attempt that the client will retry. `Attempt` is 1 for the
  first attempt, and it equals the `X-TypeSafe-Retry-Count` header of the retry that follows. The last attempt is not
  logged here, because the operation's failure event reports it. If your `CancellationToken` is cancelled while the
  client waits to retry, the retry does not happen even though the event was logged. Nor does it when the client is
  disposed before the retry starts, in which case the call returns `Disposed`.
- **`UnexpectedException`** is an Error, 1006. It means a mistake in the calling code or a bug, because a failure of the
  call itself is a returned `DecisionError`. The exception continues up to you unchanged. A cancellation that you requested
  is not logged, and neither are the argument checks and the disposed-client check that throw before the call starts.

A model listing against OpenRouter fails with `Unsupported` before any request is sent. It logs 1005, like any other
failure.

The client writes these records for its own calls. A
[hand-written `IDecisionClient`](testing-your-code.md#way-one-a-fake-idecisionclient) that relies on the interface's default
methods for typed evaluation logs nothing, because the logging lives in `DecisionClient`.

### What is never logged

The library never logs these:

- the state, the instructions and the criteria, which is everything you sent;
- the answers;
- the API key and the value of any header;
- `DecisionError.Detail`, which holds the body of the server's error response.

An exception you can still see is the one in event 1006, which carries the exception as it was thrown. That includes an
exception from a `DelegatingHandler` of your own, whose message the library cannot vouch for.

`ErrorMessage` in events 1002 and 1005 is `DecisionError.Message`, with two exceptions. The message of an `InvalidResponse`
error can quote what the server answered, and the message of a `Network` error is the transport's exception text, which
can echo the request. So those two kinds log a fixed text instead:

| Kind | `ErrorMessage` in the log |
| --- | --- |
| `InvalidResponse` | `The response could not be read.` |
| `Network` | `The request could not be sent.` |

`DecisionError.Message` itself is unchanged. If you log `result.Error.Message` yourself, remember what those two kinds can
hold.

### The cost of logging

- With no logger, or with every level the call can use disabled, such as through `NullLoggerFactory` or a filter, the
  client takes the unlogged path. It allocates nothing extra and only asks `IsEnabled`. The check is made on each call,
  so a filter that changes while the program runs is followed.
- With a logger enabled, a call that completes synchronously still allocates nothing extra. A call that completes
  asynchronously, as every real network call does, allocates the state machines of the logging wrappers, about 480 B.

[Phase 3.1](performance.md#phase-31--logging) in the performance page has the measurements. The AOT gates behind them
are `EvaluateRoundTripWithNullLoggerFactory` and `TypedEvaluateRoundTripWithEveryLevelFiltered`, which keep the budgets
of the calls without a logger, and `EvaluateRoundTripWithDiscardingLogger` and
`TypedEvaluateRoundTripWithDiscardingLogger`, which run every level. [Native AOT](native-aot.md) lists them with their
budgets.

## Traces

The client opens one span for each operation, of kind `Client`. Its name is `evaluate {gen_ai.request.model}`, for
example `evaluate jev-latest`, for an evaluation and `list_models` for a model listing. The span measures the whole
call, including the retries. A call that fails on every attempt is still one span.

To see the spans in an application, subscribe to the source named `Minos`, and nothing needs configuring on the
client. Add the source `ZeroAlloc.Rest` as well to see each HTTP attempt. The Minos span is the parent of every
attempt's span, so a call that was retried shows as one Minos span over several attempt spans.

### Subscribing with OpenTelemetry

The source and the meter share one name. Both also carry the package's informational version as their version, which
is the version of the `Minos.NET` package you installed.

<!-- snippet: Observability_OpenTelemetryNames -->
```cs
// The two names an OpenTelemetry setup needs. The source carries the spans and the meter carries the metrics.
public static class DecisionTelemetryNames
{
    public const string Source = "Minos";

    public const string Meter = "Minos";
}
```
<!-- endSnippet -->

The `Minos.NET` package takes no OpenTelemetry dependency, and neither does `Minos.NET.DependencyInjection`.
Your application brings OpenTelemetry itself, usually the `OpenTelemetry.Extensions.Hosting` package, whose
`AddOpenTelemetry()` starts the setup. Pass the source to `WithTracing` with `AddSource`, and the meter to `WithMetrics`
with `AddMeter`:

<!-- snippet: Observability_OpenTelemetryWiring -->
```cs
// Needs the OpenTelemetry.Extensions.Hosting package. Add an exporter to each builder for where the data should go.
public static IServiceCollection AddDecisionTelemetry(this IServiceCollection services)
{
    services.AddOpenTelemetry()
        .WithTracing(tracing => tracing.AddSource(DecisionTelemetryNames.Source))
        .WithMetrics(metrics => metrics.AddMeter(DecisionTelemetryNames.Meter));

    return services;
}
```
<!-- endSnippet -->

Then add an exporter to each builder for where the data should go.

OpenTelemetry is one listener. Anything built on `ActivityListener` and `MeterListener` hears the same signals, and this
one is small enough to read. The tests behind this page use it.

<!-- snippet: Observability_Listening -->
```cs
using System.Diagnostics;
using System.Diagnostics.Metrics;

public sealed record DecisionMeasurement(string Name, string? Unit, double Value, KeyValuePair<string, object?>[] Tags);

// Listens to everything Minos emits. OpenTelemetry does the same once it is told to add the source and the meter
// named Minos, and then exports what it hears.
public sealed class DecisionTelemetryListener : IDisposable
{
    private const string Name = "Minos";

    private readonly Lock _gate = new();
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters = new();

    public DecisionTelemetryListener()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, Name, StringComparison.Ordinal),

            // The sampler runs when a span starts, and sees the tags the span was created with.
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                lock (_gate)
                {
                    StartTags.Add(options.Tags is { } tags ? [.. tags] : []);
                }

                return ActivitySamplingResult.AllDataAndRecorded;
            },
            ActivityStopped = span =>
            {
                lock (_gate)
                {
                    Spans.Add(span);
                }
            },
        };
        ActivitySource.AddActivityListener(_activities);

        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, Name, StringComparison.Ordinal))
            {
                lock (_gate)
                {
                    Instruments[instrument.Name] = instrument;
                }

                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.Start();
    }

    public IList<Activity> Spans { get; } = [];

    public IList<KeyValuePair<string, object?>[]> StartTags { get; } = [];

    public IList<DecisionMeasurement> Measurements { get; } = [];

    public IDictionary<string, Instrument> Instruments { get; } = new Dictionary<string, Instrument>();

    public void Dispose()
    {
        _meters.Dispose();
        _activities.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        lock (_gate)
        {
            Measurements.Add(new DecisionMeasurement(instrument.Name, instrument.Unit, value, tags.ToArray()));
        }
    }
}
```
<!-- endSnippet -->

### The span's attributes

The attributes follow OpenTelemetry's semantic conventions for generative AI, where a convention fits. The rest are
named `minos.*`.

| Attribute | Set | Value |
| --- | --- | --- |
| `gen_ai.operation.name` | start | `evaluate` or `list_models` |
| `gen_ai.provider.name` | start | `typesafe` or `openrouter` |
| `gen_ai.request.model` | start | The requested model. Evaluations only. |
| `server.address` | start | The host of the base address. |
| `server.port` | start | The port of the base address. |
| `minos.operation` | start | `evaluate`, `evaluate-set` or `list-models`, as in the logs. |
| `minos.request.question_count` | start | The number of questions. Evaluations only. |
| `gen_ai.response.model` | success | The model that answered. Evaluations only. |
| `gen_ai.usage.input_tokens` | success | The input tokens. Evaluations only. |
| `gen_ai.usage.output_tokens` | success | The output tokens. Evaluations only. |
| `gen_ai.response.id` | success | OpenRouter's generation id, when it reports one. Only the raw `EvaluateAsync(SystemOneRequest)`. |
| `minos.usage.cost` | success | The cost in US dollars, when OpenRouter reports it. Only the raw `EvaluateAsync(SystemOneRequest)`. |
| `error.type` | failure | The name of the `DecisionErrorKind`, such as `RateLimited`, or the full type name of a thrown exception. |

The start attributes are set when the span is created, so a sampler sees them and can decide on them. A failed result
marks the span `Error`, with no description. `error.type` says what went wrong, and the description stays empty because
`DecisionError.Message` can quote the request. A typed answer that the question set rejects is an `InvalidResponse` failure,
the same as the caller sees.

The GenAI conventions name no operation for evaluating or for listing models, and no provider called TypeSafe or
OpenRouter, and they ask instrumentations to document their own. Minos uses the values in the table: the operation is
`evaluate` or `list_models`, and the provider is `typesafe` or `openrouter`.

### When an exception is thrown

A thrown exception is not a `DecisionError`, so it takes a different path. Cancellation is the usual example. Such a call
marks the span `Error` with no description, the same as a failed result, and sets `error.type` to the full name of the
exception's type, such as `System.Threading.Tasks.TaskCanceledException`. The message is left out because it is the
runtime's own text, and the library cannot vouch for it. The duration metric carries the same `error.type`, once. This
is ZeroAlloc.Telemetry 1.11.0, the library that generates the instrumentation, with
`ExceptionDescription = false` on each Minos operation.

## Metrics

The meter named `Minos` has six instruments. To collect them with OpenTelemetry, pass the name to `AddMeter`, as
[the traces section](#subscribing-with-opentelemetry) shows. Every evaluation records its duration, and a successful
evaluation also records its tokens and one confidence point for each Choice or Score answer.

| Metric | Kind | Unit | Attributes | Recorded |
| --- | --- | --- | --- | --- |
| `gen_ai.client.operation.duration` | Histogram | `s` | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `server.address`, `server.port`, `gen_ai.response.model`, `error.type` | every call |
| `gen_ai.client.inference.operation.input_tokens` | Histogram | `{token}` | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.response.model` | evaluation success |
| `gen_ai.client.inference.operation.output_tokens` | Histogram | `{token}` | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.response.model` | evaluation success |
| `gen_ai.client.inference.usage.input_tokens` | Counter | `{token}` | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.token.modality` | evaluation success |
| `gen_ai.client.inference.usage.output_tokens` | Counter | `{token}` | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.token.modality` | evaluation success |
| `minos.answer.confidence` | Histogram | `1` | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `minos.operation` | each Choice or Score answer |

- **The duration** carries `gen_ai.response.model` on success and `error.type` on failure, never both. A model listing
  has no request or response model, so its duration carries only the operation, the provider, the server and, on
  failure, the error type.
- **The token histograms** record the tokens of each evaluation. **The counters** keep the running total, and their
  `gen_ai.token.modality` is always `text`.
- **Confidence** is the `Confidence` of each Choice and Score answer, a number from 0 to 1.
  [Noul answers](question-types.md) have no confidence and record no point, so a set of Noul questions records none.
  A set with one Choice and one Score records two points for each call.

Each histogram carries bucket boundaries as advice, which an exporter uses unless you configure a view. The durations
run from `0.01` to `81.92` seconds, doubling each time. The token counts run from `1` to `67108864`, four times larger
at each step. The confidence boundaries are `0.1` to `0.9` in steps of `0.1`, then `0.95` and `0.99`.

The same values on a span and on a metric are the same strings, so you can filter a trace and a chart by the same
`gen_ai.request.model` or `error.type`.

## What is never emitted

Minos puts none of these in a span attribute, a metric attribute or a span description: the state, the instructions, the
criteria, the answers or their probabilities, the API key, a header value, `DecisionError.Message`, `DecisionError.Detail` or an
exception message. The one thing a thrown exception adds to a span is the full name of its type, as described above.

## The cost of listening

Telemetry is always compiled in. With nothing listening to the source or the meter, the instrumentation hands back each
call's own task, so the numbers are these:

- **Raw evaluation, model listing, and every call that completes synchronously:** nothing extra.
- **A typed or built-set call that completes asynchronously,** as a real network call does: one extra state machine of
  211 B, measured under the JIT. It hands back the answers and returns the pooled response buffer.
- **While listening,** a call pays for the span, its attributes and the measurements. The benchmarks measure about 1.0
  to 1.8 KB per call, depending on the path. Under Native AOT a typed call pays 1560 B, which is 4544 B listening
  against 2984 B with nothing listening.

[Phase 3.2](performance.md#phase-32--telemetry) in the performance page has the table. The AOT gates are
`EvaluateRoundTripWhileListening`, `TypedEvaluateRoundTripWhileListening` and `EvaluateBuiltSetRoundTripWhileListening`,
and [Native AOT](native-aot.md) lists their budgets. As with logging, a
[hand-written `IDecisionClient`](testing-your-code.md#way-one-a-fake-idecisionclient) that relies on the default interface methods
for typed evaluation emits nothing.

The GenAI conventions are still in development, and the token metric names follow the main branch of the
`semantic-conventions-genai` repository, which has no release yet. Names may change before this package reaches 1.0.

## Next

- [Native AOT](native-aot.md): what the library needs to run as a native executable, and the allocation budgets that
  guard it.
- [Diagnostics](diagnostics.md): the compile-time rules for question sets.
- [Performance](performance.md): the measurements behind the costs on this page.
