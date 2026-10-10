---
id: client-and-errors
title: The client and its errors
sidebar_position: 5
description: Create and configure a DecisionClient, understand its retries and time-outs, handle every kind of DecisionError, and use the raw request API.
---

# The client and its errors

Every page so far has called `EvaluateAsync` on an `IDecisionClient`. This page is about that client: how to create one, what
its options do, what happens when a call fails, and how to read the failure. It also covers the raw request API and
listing models.

The failure side matters because a call to a remote model fails in ordinary ways. The network drops, the key is wrong,
the service is busy. Minos reports every such failure as a value, a `DecisionError`, so a failure of the call itself
arrives as a value to check, not as an exception to catch.

## Creating a client

`DecisionClient` is the class that implements `IDecisionClient`. It is thread-safe and meant to live for the whole program: create
one, share it, and dispose it when the program stops. Creating a client per call wastes connections.

<!-- snippet: ClientAndErrors_Constructors -->
```cs
using Microsoft.Extensions.Logging;
using Minos;

public static class ClientConstructors
{
    // The client creates its own HttpClient, and disposing the client disposes it.
    public static DecisionClient Owned(string apiKey)
        => new(new DecisionClientOptions { ApiKey = apiKey });

    // You pass an HttpClient you manage, for example one from IHttpClientFactory. The client never disposes it.
    public static DecisionClient Borrowed(HttpClient http, string apiKey)
        => new(http, new DecisionClientOptions { ApiKey = apiKey });

    // Either form also takes an ILoggerFactory, and then logs each operation and each retried attempt.
    public static DecisionClient Logged(string apiKey, ILoggerFactory loggers)
        => new(new DecisionClientOptions { ApiKey = apiKey }, loggers);
}
```
<!-- endSnippet -->

There are two families of constructor.

- **The client owns its `HttpClient`.** `new DecisionClient(options)`, or `new DecisionClient()` for all defaults. The client
  creates the `HttpClient`, applies the options to it, and disposes it in `Dispose`.
- **You lend an `HttpClient`.** `new DecisionClient(http, options)`. The client uses it and never disposes it, so whoever
  created it still owns it. This is how a client over `IHttpClientFactory` works, and it is what a test does to put a
  fake handler under the client. Two things follow. The `HttpClient`'s own `BaseAddress` wins over
  `DecisionClientOptions.BaseAddress` when it is set, and it must end in `/`. And its own `Timeout` applies, not
  `DecisionClientOptions.Timeout`, unless you set it up with `DecisionClient.ConfigureHttpClient`, which the
  [dependency injection](dependency-injection.md#using-minos-without-the-package) page shows.

Both families take a `null` options object to mean "all defaults and environment variables", and both have an overload
that also takes an `ILoggerFactory`. With one, the client logs each operation and each retried attempt, as [Logging,
traces and metrics](observability.md#logging) describes. What it logs never contains the state, the questions, the
answers, the API key or a header value.

Two calls with bare `null` literals do not compile (CS0121), because more than one constructor accepts them.
`new DecisionClient(null)` matches both `(DecisionClientOptions?)` and `(HttpClient)`. `new DecisionClient(null, null)` matches both
`(DecisionClientOptions?, ILoggerFactory?)` and `(HttpClient, DecisionClientOptions?)`; before the logging overloads it compiled
and always threw `ArgumentNullException` for the missing `HttpClient`. For all defaults and environment variables, write
`new DecisionClient()`. Any other call resolves once its `null` has a type, for example
`new DecisionClient((DecisionClientOptions?)null)`.

A client throws instead of returning a failure only for mistakes in the calling code. A missing API key, an invalid
option or a `null` request throws when you create the client or make the call. Calling a disposed client throws
`ObjectDisposedException`. Cancelling the `CancellationToken` you passed throws `OperationCanceledException`, because
you asked for it. Everything else, including every network and service failure, comes back as a `DecisionError`.

### Disposing a client

`Dispose` decides what happens to a call by when the call started:

- **A call started after `Dispose`** throws `ObjectDisposedException`, because calling a disposed client is a mistake in
  the calling code.
- **A call already in flight** over an `HttpClient` the client created is torn down with that `HttpClient`. It returns
  a `DecisionError` of kind `Disposed`, not `Timeout` or `Network`, so you can tell it apart from a real failure. A real
  time-out that was mapped before `Dispose` set its flag keeps `Timeout` when no retry is left.
- **A call already in flight over an `HttpClient` you lent** is not torn down, because the client never disposes that
  `HttpClient`. The attempt in flight keeps its own result.
- **A disposed client never retries**, whichever kind of `HttpClient` it uses. A retry that would start after `Dispose`
  is not sent, and the call returns `Disposed`.

## Options

`DecisionClientOptions` has ten properties, and every one is optional.

| Property | Default | Valid values | What it does |
| --- | --- | --- | --- |
| `Provider` | `DecisionProvider.TypeSafe` | `TypeSafe` or `OpenRouter` | Where requests go. |
| `ApiKey` | none | any text without control characters; blank counts as unset | The key sent as a bearer token. When unset, the client reads `TYPESAFE_API_KEY`, or `OPENROUTER_API_KEY` for OpenRouter. |
| `BaseAddress` | the provider's address | absolute `http` or `https` URI, no query or fragment | The API root, for a proxy or a test server. When unset, the client reads `TYPESAFE_BASE_URL` for TypeSafe only, then uses `https://api.typesafe.ai/` or `https://openrouter.ai/api/`. |
| `Model` | `jev-latest` | not blank | The model that typed evaluation and built question sets ask: an alias such as `jev-latest` (or TypeSafe's `jev-preview`), or a versioned id such as `jev-1.13.0`. See [Listing models](#listing-models). |
| `Timeout` | 60 seconds | positive, or `Timeout.InfiniteTimeSpan`, and at most about 24.8 days | How long one attempt may take. |
| `MaxRetries` | 2 | 0 to 10 | How many times a failed call is tried again. 0 turns retries off. |
| `InitialBackoff` | 500 ms | positive, and at most about 24.8 days | The first wait between attempts. It doubles for each further retry. |
| `MaxRetryDelay` | 30 seconds | at least `InitialBackoff`, and at most about 24.8 days | The longest wait between attempts, for the backoff and for a server's `Retry-After` alike. |
| `Jitter` | `true` | `true` or `false` | Adds a random extra of up to 50 percent to each backoff wait, so many clients do not retry in step. |
| `UseStandardPipeline` | `true` | `true` or `false` | Whether a `DecisionRequest` call runs through the standard [pipeline](pipeline.md) of telemetry, logging and retries. `false` leaves one attempt per call, for a pipeline of your own. |

Each option you set beats the environment. The order is: the option, then the environment variable, then the provider's
default. A blank API key counts as unset. `TYPESAFE_BASE_URL` never applies to OpenRouter, so an OpenRouter key is
never sent to a TypeSafe proxy.

The first snippet spells out every option that has a default, with its default value. You rarely write all of them. It
is here so the table above has code to match.

<!-- snippet: ClientAndErrors_Options -->
```cs
// Every option set to the value it has when you leave it out, apart from the API key, which has no default:
// without one, the client reads TYPESAFE_API_KEY, or OPENROUTER_API_KEY for OpenRouter.
// BaseAddress is also left out: it defaults to the provider's own address.
public static DecisionClientOptions SpelledOut(string apiKey)
    => new()
    {
        ApiKey = apiKey,
        Provider = DecisionProvider.TypeSafe,
        Model = "jev-latest",
        Timeout = TimeSpan.FromSeconds(60),
        MaxRetries = 2,
        InitialBackoff = TimeSpan.FromMilliseconds(500),
        MaxRetryDelay = TimeSpan.FromSeconds(30),
        Jitter = true,
        UseStandardPipeline = true,
    };
```
<!-- endSnippet -->

A `SystemOneRequest` names its own model, so `Model` applies to typed evaluation and to question sets only.

### DecisionDefaults

`DecisionDefaults` is the static class that holds the values the client falls back on. Use it instead of typing the same
text yourself, for example to send `DecisionDefaults.Model` explicitly or to name an environment variable in a message.

| Member | Value | What it is |
| --- | --- | --- |
| `ApiKeyEnvironmentVariable` | `TYPESAFE_API_KEY` | The variable a TypeSafe key is read from. |
| `OpenRouterApiKeyEnvironmentVariable` | `OPENROUTER_API_KEY` | The variable an OpenRouter key is read from. |
| `BaseAddressEnvironmentVariable` | `TYPESAFE_BASE_URL` | The variable that overrides the base address, for TypeSafe only. |
| `Model` | `jev-latest` | The model alias used when a request does not name one. A property, so a changed default is not compiled into your code. |
| `TypeSafeBaseAddress` | `https://api.typesafe.ai/` | The root address of TypeSafe's API. A property. |
| `OpenRouterBaseAddress` | `https://openrouter.ai/api/` | The root address of OpenRouter's System One API. A property. |

### Checking options early

The constructors check the options, and so does `Validate()`. It runs exactly the check a constructor runs, including
the API key and base address environment variables, but creates no client. Use it in a health check, or to fail fast on
start-up.

<!-- snippet: ClientAndErrors_Validate -->
```cs
// Validate runs the check the constructors run, without creating a client. It throws ArgumentException for a value
// that is out of range, and InvalidOperationException when no API key can be found.
public static string? Problem(DecisionClientOptions options)
{
    try
    {
        options.Validate();
        return null;
    }
    catch (ArgumentException exception)
    {
        return exception.Message;
    }
    catch (InvalidOperationException exception)
    {
        return exception.Message;
    }
}
```
<!-- endSnippet -->

An invalid option throws `ArgumentException`. A missing API key, or an invalid key or base address environment variable,
throws `InvalidOperationException`. The message says which option or variable is wrong. If a value could pass here and
fail in the constructor, it can only be because an environment variable changed in between.

## Retries

Minos retries a failed call for you. For a typed, built-set or `DecisionRequest` call, the retries are the retry stage
of the client's [pipeline](pipeline.md), `RetryingDecisionClient`, which the client builds from the options below. The
raw System One calls retry with the same settings. These failures are retried:

- rate limiting, HTTP 429;
- overload, HTTP 503 and 529;
- any other server error, HTTP 5xx;
- request time-out, HTTP 408;
- a network failure, such as a refused connection;
- a time-out of the client's own.

Everything else is reported at once: a rejected key (401, 403), an invalid request (400, 422), and any other status.
Trying those again would give the same answer.

With `UseStandardPipeline` set to `false`, nothing is retried, the raw calls included, until you add a retry stage
yourself with `UseRetries()`. [Building your own pipeline](pipeline.md#building-your-own-pipeline) shows how, and how
to retry other kinds of failure with `DecisionRetryOptions.ShouldRetry`.

The wait before the first retry is `InitialBackoff`, and it doubles each time, up to `MaxRetryDelay`. With the defaults
the waits are about 0.5 s, then 1 s, and a call makes at most three attempts. With `Jitter` on, each wait gains a random
extra of up to half its length, and a wait never exceeds `MaxRetryDelay`. Each retry also sends the attempt number in an
`X-TypeSafe-Retry-Count` header, as TypeSafe's own SDKs do. The first attempt does not send it.

<!-- snippet: ClientAndErrors_Retries -->
```cs
// Four retries, waiting about 1 s, 2 s, 4 s and then 8 s, each wait at most 20 s.
public static DecisionClientOptions Patient(string apiKey)
    => new()
    {
        ApiKey = apiKey,
        MaxRetries = 4,
        InitialBackoff = TimeSpan.FromSeconds(1),
        MaxRetryDelay = TimeSpan.FromSeconds(20),
    };

// No retries: a failed call is reported at once. Use this where a duplicate, billed request is worse than a failure.
public static DecisionClientOptions NoRetries(string apiKey)
    => new() { ApiKey = apiKey, MaxRetries = 0 };
```
<!-- endSnippet -->

### Retry-After

A busy service usually says how long to wait. Minos reads two headers: `retry-after-ms`, in milliseconds, and the
standard `Retry-After`, either as a number of seconds or as an HTTP date. When both are present, `retry-after-ms` wins,
provided it is a non-negative number. An invalid `retry-after-ms` is ignored and `Retry-After` is used. The wait
replaces the backoff for the next attempt and is waited for as asked, never beyond `MaxRetryDelay`. A server that asks
for an hour is waited for `MaxRetryDelay`, 30 seconds by default, and then asked again. The value is also available to
you, as `DecisionError.RetryAfter`, on a failure that comes back.

TypeSafe's [API reference](https://docs.typesafe.ai/api) asks clients to back off exponentially on a 429 or 529, and
does not say whether those responses carry either header; its official Python SDK reads both. This library's own live
runs have not yet met a 429 or 529, so as of October 2026 whether TypeSafe sends a wait is unconfirmed. Either way the
client behaves correctly: it waits as asked when a header is present, and backs off when none is.

### The cost of retrying

Retrying can be billed twice. When a time-out, a network failure or a 5xx happens after the request reached the server,
the server may already have processed it, and it may charge for it. Retrying then sends a second request that is
processed, and charged, again. If a duplicate charge matters more to you than resilience, set `MaxRetries = 0`.

If you route the client through a handler that retries by itself, such as a standard resilience handler, set
`MaxRetries = 0` too. Otherwise the two sets of retries multiply: three attempts of the client inside each of three
attempts of the handler is nine requests.

## Time-outs

`Timeout` bounds one attempt, not the whole call. A call with retries can take up to `(MaxRetries + 1) times Timeout`
plus the waits between attempts. Its time-out applies to a client that owns its `HttpClient`, and to one you set up with
`DecisionClient.ConfigureHttpClient`, as `AddDecisionClient` does. An `HttpClient` you pass without that keeps its own `Timeout`,
which is 100 seconds unless you changed it.

<!-- snippet: ClientAndErrors_Timeouts -->
```cs
// Each attempt may take 10 s, and there is one retry. The worst case for one call is therefore two attempts of
// 10 s each, plus one wait of at most MaxRetryDelay between them.
public static DecisionClientOptions Strict(string apiKey)
    => new() { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(10), MaxRetries = 1 };
```
<!-- endSnippet -->

An attempt that takes longer than `Timeout` fails with `DecisionErrorKind.Timeout`, and is retried like any transient
failure. Cancelling your own `CancellationToken` is different: it stops the call at once and throws, with no retry.

## Errors

A failed call returns a `DecisionError`. Its `Kind` says what went wrong, and the other members add detail when there is any.

You can build one yourself, for a test fake, with `new DecisionError(kind, message)`. `StatusCode`, `RetryAfter`, `Detail`
and `Exception` are `init` properties, so set only the ones that apply with an object initializer.

| Member | Holds |
| --- | --- |
| `Kind` | A `DecisionErrorKind`: the cause, listed below. |
| `Message` | A short description in English. The response body is in `Detail`, not here. |
| `StatusCode` | The HTTP status, or `null` when no response arrived. |
| `RetryAfter` | How long the service asked you to wait, or `null`. |
| `Detail` | The error response body as a `JsonElement`, when it is JSON, for example the field a 422 rejected. |
| `Exception` | The exception behind a network, time-out or unreadable-response failure. |
| `Failures` | For `InvalidQuestions` only, the rules a question set breaks. Empty for every other kind. |

`Detail` holds whatever JSON the service sent, and its shape depends on the error. TypeSafe answers a 422 with a list
of problems, each with the path of the field (`loc`) and what is wrong with it (`msg`). This is a real one, for a
request with no questions:

```json
{"detail":[{"type":"too_short","loc":["body","questions"],"msg":"Dictionary should have at least 1 item after validation, not 0","input":{},"ctx":{"field_type":"Dictionary","min_length":1,"actual_length":0}}]}
```

A 401, by contrast, carries one object: `{"detail":{"error_type":"authentication_error","message":"…"}}`, and
OpenRouter's errors use its own shape, `{"error":{"message":"…","code":400}}`. Check the shape before reading it:

<!-- snippet: ClientAndErrors_ValidationProblems -->
```cs
// TypeSafe answers a 422 with a list of problems. Each one says where it is, as a path such as body.questions,
// and what is wrong. Other errors carry a different body, and a problem may be malformed, so check each shape.
public static IReadOnlyList<string> List(DecisionError error)
{
    var problems = new List<string>();
    if (error is not { Kind: DecisionErrorKind.Validation, Detail: { ValueKind: JsonValueKind.Object } body }
        || !body.TryGetProperty("detail", out var detail)
        || detail.ValueKind != JsonValueKind.Array)
    {
        return problems;
    }

    foreach (var problem in detail.EnumerateArray())
    {
        // Skip an entry that is not shaped like a problem rather than fail while handling an error.
        if (problem.ValueKind != JsonValueKind.Object
            || !problem.TryGetProperty("loc", out var loc) || loc.ValueKind != JsonValueKind.Array
            || !problem.TryGetProperty("msg", out var msg) || msg.ValueKind != JsonValueKind.String)
        {
            continue;
        }

        var path = new List<string>();
        foreach (var part in loc.EnumerateArray())
        {
            path.Add(part.ToString());
        }

        problems.Add($"{string.Join('.', path)}: {msg.GetString()}");
    }

    return problems;
}
```
<!-- endSnippet -->

`ToString()` gives a one-line form, such as `Overloaded (503): The API returned HTTP 503.`

### The kinds

The values start at 1, so `default(DecisionErrorKind)` is no kind.

| `DecisionErrorKind` | When | Retried | What to do |
| --- | --- | --- | --- |
| `Unauthorized` | HTTP 401 or 403: the key is missing, wrong or lacks access. | No | Fix the key. Retrying cannot help. |
| `Validation` | HTTP 400 or 422: the request was rejected. `Detail` usually names the field. | No | Fix the request. This is a bug in the questions or the state. |
| `RateLimited` | HTTP 429. | Yes | Slow down. It comes back only after the retries ran out, and `RetryAfter` says how long to wait. |
| `Overloaded` | HTTP 503 or 529. | Yes | Try again later, or fall back. |
| `Server` | Any other HTTP 5xx. | Yes | Treat it as an outage. |
| `Http` | Any other unsuccessful status, such as 404, or 408. | Only 408 | Report it. The status is in `StatusCode`. |
| `Network` | No response: a DNS, connection or TLS failure. | Yes | Check connectivity. `Exception` holds the cause. |
| `Timeout` | An attempt exceeded `Timeout`. | Yes | Raise `Timeout`, or ask fewer questions. |
| `InvalidResponse` | A successful response could not be read as the expected JSON, or an answer was missing. `StatusCode` is then 200, or the other 2xx status that arrived. | No | Report it. The service replied with something this library does not understand. |
| `Unsupported` | The operation is not available on the provider, such as listing models on OpenRouter. | No | Do not call it on that provider. No request was sent. |
| `InvalidQuestions` | A question set built at run time breaks the API's rules. Only a failed `Build()` returns it, and no request is sent. | No | Fix the set. `Failures` lists each rule. |
| `Disposed` | The client was disposed while the call was in flight, which tore its request down or kept a due retry from being sent. | No | Stop. The client is gone. A call started after disposal throws `ObjectDisposedException` instead. |

The "Retried" column describes what the client does before it returns the error. A `RateLimited`, `Overloaded`,
`Server`, `Network` or `Timeout` error, or an `Http` error for a 408, that reaches you has already been retried
`MaxRetries` times.

### Handling a failure

A `switch` over `Kind` turns each failure into an action. It needs a catch-all arm, because later versions can add
kinds.

<!-- snippet: ClientAndErrors_Failures -->
```cs
// Every failure of a call comes back as a DecisionError. Kind says what to do about it. The other members
// add detail when there is some: StatusCode, RetryAfter, Detail and Exception.
public static string Describe(DecisionError error) => error.Kind switch
{
    DecisionErrorKind.Unauthorized => "The service rejected the API key. Check the key and what it may access.",
    DecisionErrorKind.Validation => $"The service rejected the request: {error.Detail?.GetRawText() ?? error.Message}",
    DecisionErrorKind.RateLimited or DecisionErrorKind.Overloaded when error.RetryAfter is { } wait
        => $"The service is busy. It asks for {(int)wait.TotalMilliseconds} ms before the next call.",
    DecisionErrorKind.RateLimited or DecisionErrorKind.Overloaded => "The service is busy. Try again later.",
    DecisionErrorKind.Server or DecisionErrorKind.Http => $"The service failed with HTTP {error.StatusCode}: {error.Message}",
    DecisionErrorKind.Network or DecisionErrorKind.Timeout => $"The service could not be reached: {error.Exception?.Message ?? error.Message}",
    DecisionErrorKind.InvalidResponse => $"The service replied with something unreadable: {error.Message}",
    DecisionErrorKind.Unsupported => $"The provider cannot do that: {error.Message}",
    DecisionErrorKind.InvalidQuestions => $"The question set is invalid, {error.Failures.Count} rules broken.",
    DecisionErrorKind.Disposed => "The client was disposed while the call was running.",

    // A kind added in a later version still produces a useful message.
    _ => error.ToString(),
};
```
<!-- endSnippet -->

The same `DecisionError` comes back from every evaluate call: typed, built at run time or raw. A
[fake `IDecisionClient`](testing-your-code.md#way-one-a-fake-idecisionclient) in a test can return one too.

## The raw request API

[Typed evaluation](typed-evaluation.md) and [question sets built at run time](question-sets-at-run-time.md) cover
most uses. Underneath them is the raw API, the shape of the HTTP request: a state, a model and a dictionary of questions
with ids you choose. You rarely need it, but it is there when you want the exact wire form, or are building your own
layer.

The raw calls, `EvaluateAsync(SystemOneRequest)` and `ListModelsAsync`, are members of `DecisionClient`, not of
`IDecisionClient`. Code that holds an `IDecisionClient`, such as a class that takes one from dependency injection,
reaches them with `client.GetService<DecisionClient>()`, which finds the `DecisionClient` under any stages wrapped
around it and returns `null` when there is none. [Finding a stage or the
transport](pipeline.md#finding-a-stage-or-the-transport) has an example. The raw calls do not run through the
pipeline's stages: they have their own span and log, and retry with the client's settings.

<!-- snippet: ClientAndErrors_RawRequest -->
```cs
// The raw API names its own model and questions, with ids you choose. Use it when the questions are not known at
// compile time and the question set builder does not fit. Typed evaluation is shorter wherever it can be used.
public static async Task<string> UrgencyAsync(DecisionClient client, string message, CancellationToken cancellationToken)
{
    var result = await client.EvaluateAsync(
        new SystemOneRequest
        {
            State = message,
            Questions = new Dictionary<string, Question>
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        },
        cancellationToken);

    if (result.IsFailure)
    {
        return ClientFailures.Describe(result.Error);
    }

    // Answers are keyed by the ids from the request, and each one is a NoulAnswer, ChoiceAnswer or ScoreAnswer.
    var response = result.Value;
    return response.Answers["is_urgent"] is NoulAnswer urgent
        ? $"{response.Model}: {urgent.Noul * 100:0} %, {response.Usage.InputTokens} input tokens"
        : "Unexpected answer type.";
}
```
<!-- endSnippet -->

A `SystemOneRequest` has a required `State`, which is a `DecisionContent` (text converts to one, as
[typed evaluation](typed-evaluation.md#decisioncontent) describes), a `Model` that defaults to `jev-latest`, and required
`Questions`. The response is a `SystemOneResponse`: the versioned `Model` that answered, `Answers` keyed by the ids you
sent, and `Usage` with the input and output token counts. Only input tokens are billed. On OpenRouter the response also
carries the generation `Id` and the upstream `Provider`, and `Usage` carries the `Cost` in US dollars.

Each answer is a `NoulAnswer`, a `ChoiceAnswer` or a `ScoreAnswer`, so you pattern-match to read it. The typed forms do
that matching for you, which is the reason to prefer them. The raw API leaves the API's rules for questions to you,
where the generator, the [analyzers](diagnostics.md) and the
[question set builder](question-sets-at-run-time.md) check them for you. A question the service rejects comes back as a
`Validation` error.

### When your code has its own Question or Answer

The raw API's base types are `Minos.Question` and `Minos.Answer`. If your application has a `Question` or `Answer` of
its own, a file that imports both namespaces cannot name either type bare: the compiler reports CS0104, an ambiguous
reference. A `using` alias settles it, because an alias outranks a namespace import. Alias your own type to the short
name, and give Minos's type a name of its own.

<!-- snippet: ClientAndErrors_NameClash -->
```cs
using Minos;
using Shop.Surveys;

// Shop.Surveys and Minos both have a Question, so a bare Question in this file would be ambiguous: error CS0104. An
// alias outranks a using of a whole namespace, so these two settle it. Question is your own, MinosQuestion is Minos's.
using Question = Shop.Surveys.Question;
using MinosQuestion = Minos.Question;

public static class SurveyRequests
{
    // Each survey question becomes a yes/no question in a raw request, under the survey question's own key.
    public static SystemOneRequest ToRequest(Survey survey, string state)
    {
        var questions = new Dictionary<string, MinosQuestion>(StringComparer.Ordinal);
        foreach (Question question in survey.Questions)
        {
            questions[question.Key] = new NoulQuestion { Instructions = question.Text };
        }

        return new SystemOneRequest { State = state, Questions = questions };
    }
}
```
<!-- endSnippet -->

Code in a namespace under `Minos` never sees the clash: it finds Minos's types first.

## Listing models

`ListModelsAsync` returns the models and aliases your account can name in a `SystemOneRequest`: a `ModelList` of
`ModelCard`s, each with a `Name`, a `Description` and a `ReleaseDate`.

<!-- snippet: ClientAndErrors_Models -->
```cs
public static async Task<string> ModelsAsync(DecisionClient client, CancellationToken cancellationToken)
{
    var result = await client.ListModelsAsync(cancellationToken);
    if (result.IsFailure)
    {
        return ClientFailures.Describe(result.Error);
    }

    var names = new List<string>();
    foreach (var model in result.Value.Models)
    {
        names.Add($"{model.Name} ({model.ReleaseDate})");
    }

    return string.Join(", ", names);
}
```
<!-- endSnippet -->

Listing models is available on TypeSafe's API only. On OpenRouter the call returns an `Unsupported` error and sends no
request, because OpenRouter has its own models API.

As of October 2026, TypeSafe lists only the aliases, `jev-latest` for the newest stable release and `jev-preview` for
the newest release of any kind. A versioned id such as `jev-1.13.0` is accepted as a `Model` too, listed or not, as
TypeSafe's [models page](https://docs.typesafe.ai/models) describes. Whichever you send, the response's `Model` names
the versioned model that answered, so log it if you need to know which release produced a result.

Through OpenRouter, `jev-latest` works and the response reports OpenRouter's own model id, such as
`typesafe/jev-1.13-20260917`. As of October 2026 OpenRouter does not offer `jev-preview`: a request for it fails with
`Validation` and HTTP 400, and OpenRouter's body says the model `typesafe/jev-preview` does not exist.

## Next

- [Dependency injection](dependency-injection.md): register the client in a .NET host, with several clients, bound from
  configuration and checked at start-up.
- [Typed evaluation](typed-evaluation.md): the typed calls that return these errors.
- [Performance](performance.md): what a call costs, measured.
