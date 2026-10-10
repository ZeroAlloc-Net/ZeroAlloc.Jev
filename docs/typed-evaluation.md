---
id: typed-evaluation
title: Typed evaluation
sidebar_position: 3
description: Declare questions as a C# type, give them a typed state, and pick the EvaluateAsync overload.
---

# Typed evaluation

Typed evaluation is the main way to use Minos.NET. You declare the questions once, as a partial record. At compile
time a source generator writes the question definition, which the client turns into the request, and a `Create` method
that builds the record from the answers the client reads back. So a call is one line, and the answers come back as
typed properties. This page covers the declaration, the state you hand to Jev, and the ways to call it.
[Getting started](getting-started.md) has the shortest working example, and [Question types](question-types.md) covers
what each answer holds.

When the questions are only known at run time, use [question sets built at run time](question-sets-at-run-time.md)
instead.

## The state

Jev judges a **state**: the thing the questions are about. It can be plain text, or any JSON object, array or string.
When the state has a shape of its own, such as a support ticket, describe it with a type and let the library serialize
it.

The type needs JSON metadata generated at compile time, so that serializing it takes no reflection and works under
[Native AOT](native-aot.md#state-types-need-json-metadata). A `JsonSerializerContext` provides that.

<!-- snippet: TypedEvaluation_State -->
```cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Minos;

// The state is what Jev reads: any type that serializes to a JSON object, array or string.
public sealed record SupportTicket(string Subject, string Body, string Plan);

// Source-generated JSON metadata for the state, so serializing it needs no reflection.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SupportTicket))]
internal sealed partial class SupportTicketJson : JsonSerializerContext;
```
<!-- endSnippet -->

The context uses camel-case names, so the ticket goes over the wire as `subject`, `body` and `plan`. That matters for
the backticked names in the next section.

## Declaring the questions

Mark a partial record with `[Questions]`, and declare each question as a partial, get-only property with the
matching attribute.

| Property type | Attribute | What it asks |
| --- | --- | --- |
| `Noul` | `[Noul("...")]` | A yes/no question, answered with a probability. |
| `Choice<TEnum>` | `[Choice("...")]` | One option out of the enum's members. |
| `Score<TEnum>` | `[Score("...")]` | A level on an ordered scale, one per enum member. |

The attribute's text is the question, in plain language. The question's key on the wire is the property name in
snake_case, and `Key = "..."` on the attribute sets another one. A key can be used only once in a set.

<!-- snippet: TypedEvaluation_Questions -->
```cs
public enum Desk
{
    [Criteria(
        "Payments, invoices and refunds",
        Examples = ["I was charged twice"],
        NotFor = ["How much is the Pro plan?"])]
    Billing,

    [Criteria("Bugs, outages and integrations", Examples = ["The API returns a 500"])]
    Technical,

    [Criteria("Pricing, upgrades and new accounts")]
    Sales,

    // A member with no [Criteria] is a valid option, sent with no description.
    Other,
}

public enum Impact
{
    [Level("Cosmetic or minor", NotFor = ["Data loss"])]
    Low,

    [Level("Slows the customer down")]
    Medium,

    [Level("Blocks the customer's work")]
    High,
}

// State = typeof(SupportTicket) links the questions to the state type. The backticked names in the instructions
// are checked against that type's members when you build.
[Questions(State = typeof(SupportTicket))]
public partial record TicketReview
{
    [Noul(
        "Is the `body` urgent, for a customer on the `plan` they have?",
        WhenTrue = "The customer needs help right away",
        WhenFalse = "The customer can wait")]
    public partial Noul IsUrgent { get; }

    [Choice("Which desk should handle this?")]
    public partial Choice<Desk> Desk { get; }

    [Score("How much does this affect the customer?", Key = "impact")]
    public partial Score<Impact> Impact { get; }
}
```
<!-- endSnippet -->

A few things in that declaration are worth reading closely.

- **Options and levels are enum members.** `[Criteria]` describes a Choice option. `[Level]` describes a Score level,
  and the members, in declaration order, are the levels from lowest to highest. A Choice member without `[Criteria]` is
  still an option and is sent with no description, though the analyzers report it as the Info diagnostic
  [MIN006](diagnostics.md#the-rules). A Score level must have a `[Level]`, because the API does not accept a level
  without one.
- **`Examples` and `NotFor` sharpen a description.** `Examples` lists texts that belong to the option, and `NotFor`
  lists texts that only look as if they do. With either set and non-empty, the generator sends a criterion object in
  place of a plain string for that option or level. Empty arrays and `null` entries are left out.
- **`WhenTrue` and `WhenFalse` describe a Noul's answers.** They say what a yes and a no mean for that question. Both
  are optional.
- **`State = typeof(SupportTicket)` links the set to its state type.** The generated type then implements
  `IQuestionSet<TicketReview, SupportTicket>`. The type must be a class, struct, record or array type.

`description`, `examples` and `not_for` are a convention of this library that the model reads from the criterion
object. The Jev API itself has no such fields. The generated questions for `TicketReview` look like this:

```json
{
  "is_urgent": {
    "type": "noul",
    "instructions": "Is the `body` urgent, for a customer on the `plan` they have?",
    "criteria": { "true": "The customer needs help right away", "false": "The customer can wait" }
  },
  "desk": {
    "type": "choice",
    "instructions": "Which desk should handle this?",
    "criteria": {
      "billing": {
        "description": "Payments, invoices and refunds",
        "examples": ["I was charged twice"],
        "not_for": ["How much is the Pro plan?"]
      },
      "technical": { "description": "Bugs, outages and integrations", "examples": ["The API returns a 500"] },
      "sales": "Pricing, upgrades and new accounts",
      "other": null
    }
  },
  "impact": {
    "type": "score",
    "instructions": "How much does this affect the customer?",
    "criteria": [
      { "description": "Cosmetic or minor", "not_for": ["Data loss"] },
      "Slows the customer down",
      "Blocks the customer's work"
    ]
  }
}
```

A Choice's options are keyed by the member name in snake_case, or by `Key` on the member's `[Criteria]` when one is
set. A Score's levels are keyed by their index, and `[Level]` has no `Key`. The generator
fixes the question text and criteria in the definition it writes at compile time, and the client writes this JSON from
it: they cannot vary per call. What varies per call is the state.

### Referring to the state in a question

A question can point at a member of the state by putting its name in backticks, as `` `body` `` and `` `plan` `` do
above. The generator checks each backticked name against the state type's public instance properties and fields,
including inherited ones. A name matches the member's own name, its snake_case or kebab-case form, or its
`[JsonPropertyName]`, ignoring case. A name that matches nothing is reported as warning
[MIN004](diagnostics.md#the-rules), so a renamed member shows up at compile time and not as a quietly confused question.
For a state that is an array, the element type is checked.

The check only runs for a set with a `State` type.

### Structured instructions

A declared question's instructions, criteria and levels are always text. For instructions or a description that is a
JSON object or array, such as a policy or a few labelled facts next to the question, build the set at run time: pass
a [`DecisionContent`](#decisioncontent) as the instructions, and describe an option with `Criterion.Json`.
[Question sets at run time](question-sets-at-run-time.md#describing-options) shows both.

## Evaluating

Call `EvaluateAsync<T>` on an `IDecisionClient`. It returns a `Result` and not the answers, because a call can fail in
many ways. Check `IsFailure` before reading `Value`.

<!-- snippet: TypedEvaluation_Evaluate -->
```cs
public static async Task<(bool Urgent, Desk Desk, Impact Impact)?> ReviewAsync(
    IDecisionClient client, SupportTicket ticket, CancellationToken cancellationToken)
{
    // The state type and its JSON metadata travel together. The call serializes the ticket and sends it
    // with the questions.
    var result = await client.EvaluateAsync<TicketReview, SupportTicket>(
        ticket, SupportTicketJson.Default.SupportTicket, cancellationToken);
    if (result.IsFailure)
    {
        return null;
    }

    var review = result.Value;
    return (review.IsUrgent.Value, review.Desk.Value, review.Impact.Value);
}
```
<!-- endSnippet -->

`EvaluateAsync<TicketReview, SupportTicket>` takes the state, its JSON metadata and an optional cancellation token.
The call needs the `State` link: a set declared without `State = typeof(SupportTicket)` does not satisfy the
constraint, so a mismatched state and question set is a compile error. A cancelled call throws
`OperationCanceledException`. The `TState` overload throws `ArgumentNullException` for a `null` state or a `null`
`JsonTypeInfo`.

The overloads differ in the state they take.

| Call | State you pass |
| --- | --- |
| `EvaluateAsync<T>(string, ...)` | Plain text. The string must not be `null`. |
| `EvaluateAsync<T>(JsonElement, ...)` | A JSON string, object or array already parsed. |
| `EvaluateUtf8Async<T>(ReadOnlyMemory<byte>, ...)` | UTF-8 JSON that holds exactly one string, object or array. |
| `EvaluateAsync<T, TState>(TState, JsonTypeInfo<TState>, ...)` | A typed object, serialized with the metadata you pass. |

The first three work for any question set, with or without a state type. A set that has one accepts them as well. The
text overload is the one to reach for when the state is a message or a document. The `JsonElement` and UTF-8 overloads
suit state you already hold as JSON, such as a request body.

The first three overloads need a set to evaluate. This one declares no `State` type, so any text or JSON can be its
state.

<!-- snippet: TypedEvaluation_Stateless -->
```cs
// Without State, the questions stand alone, and any text or JSON can be the state.
[Questions]
public partial record UrgencyCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}
```
<!-- endSnippet -->

<!-- snippet: TypedEvaluation_OtherStates -->
```cs
// A question set without a State type takes its state as text, as a JsonElement or as UTF-8 JSON.
public static async Task<int> EvaluateEachFormAsync(
    IDecisionClient client,
    string text,
    JsonElement element,
    ReadOnlyMemory<byte> utf8Json,
    CancellationToken cancellationToken)
{
    var succeeded = 0;

    var fromText = await client.EvaluateAsync<UrgencyCheck>(text, cancellationToken);
    succeeded += fromText.IsSuccess ? 1 : 0;

    var fromElement = await client.EvaluateAsync<UrgencyCheck>(element, cancellationToken);
    succeeded += fromElement.IsSuccess ? 1 : 0;

    var fromUtf8 = await client.EvaluateUtf8Async<UrgencyCheck>(utf8Json, cancellationToken);
    succeeded += fromUtf8.IsSuccess ? 1 : 0;

    return succeeded;
}
```
<!-- endSnippet -->

A state that is not a JSON string, object or array, such as the number `42`, is a programming error. The overloads
throw `ArgumentException` for it, and do not return a failed `Result`. A `null` string throws
`ArgumentNullException`. Failures that can happen in correct code, such as a rejected key or an unreadable reply,
come back in the `Result`. In particular, a response that is missing an answer, or that has one of the wrong type, is a
failure with kind `DecisionErrorKind.InvalidResponse`.

The model is the one in `DecisionClientOptions.Model`, which defaults to the alias `jev-latest`.

### How a typed call reaches the client

The overloads are extension methods in `DecisionClientExtensions`, in the `Minos` namespace, so they work on any
`IDecisionClient`. Each one builds a `DecisionRequest` from the set's generated `Definition` and the state, makes the
client's one call, `EvaluateAsync(DecisionRequest)`, and builds the typed answers from the `DecisionResponse` with the
generated `Create`. So a typed call runs through the client's [pipeline](pipeline.md), with its telemetry, logging and
retries, and a hand-written fake in a test answers it by implementing that one call, as
[Testing your code](testing-your-code.md#way-one-a-fake-idecisionclient) shows.

Inside a `DecisionClient`, the protocol writes the request from the definition into pooled buffers, and caches the
questions JSON of each set after the first call.

Each overload sends its state as it did before the [pipeline](pipeline.md). `EvaluateUtf8Async<T>` copies your bytes
into the request as they are, with their whitespace and escapes, and parses nothing. The `JsonElement` overload writes
your element without copying it first. Both read your state until the returned task completes, because a retry writes
the request again, so don't change the bytes or dispose the document before then. `EvaluateAsync<T, TState>`
serializes the state once per call, with the request's own writer settings, so the request carries the bytes your
`JsonTypeInfo<TState>` writes, escaped as the request escapes them. That costs the serialized bytes and the writer,
about 290 B for the smoke application's state under Native AOT, and
[Performance](performance.md#phase-63--the-client-pipeline) has the figures. The other overloads add nothing for an
object, an array or text.

## DecisionContent

`DecisionContent` is the type the library uses for a value that is either text or structured JSON. A typed call builds
one for you. You meet it directly when you [build a question set at run time](question-sets-at-run-time.md),
where the state, each question's instructions and each description are all `DecisionContent`.

<!-- snippet: TypedEvaluation_Content -->
```cs
// DecisionContent holds a state, a question's instructions or a description: text, or a JSON object or array.
public static DecisionContent[] ContentForms(SupportTicket ticket)
{
    return
    [
        DecisionContent.FromString("Help! My payouts have been failing for 3 days."),
        DecisionContent.FromValue(ticket, SupportTicketJson.Default.SupportTicket),
        DecisionContent.FromUtf8Json("""{"subject":"Payouts failing"}"""u8),
    ];
}
```
<!-- endSnippet -->

- `DecisionContent.FromString` makes text content. A string also converts to a `DecisionContent` implicitly.
- `DecisionContent.FromValue` serializes a value through its source-generated `JsonTypeInfo<T>`.
- `DecisionContent.FromUtf8Json` parses UTF-8 JSON you already have. The content does not keep a reference to the bytes.
- `DecisionContent.FromJson` wraps a `JsonElement`.

JSON content must be a single string, object or array. A JSON string becomes text content. The factory methods throw
`ArgumentException` for a number, `true`, `false` or `null`, and for malformed input, empty input or more than one
value. `TryGetString` and `TryGetJson` read the content back.

## What the package ships

`Minos.NET` carries the source generator and the [analyzers](diagnostics.md), so there is nothing else to install.
The generator writes `Definition` and `Create` for each `[Questions]` type. The analyzers check the declaration as
you type: the shape of the type, the keys, the enums and the backticked names. A set with an error gets no generated
members, and the generator stubs its properties, so the build reports the analyzer's error and not a confusing
missing-implementation one.

The type that carries `[Questions]` must be a non-generic, non-abstract, non-static, top-level partial class or
record, and it needs a constructor that can be called without arguments. Its question properties must be partial,
get-only instance properties.

## Next

- [Question sets built at run time](question-sets-at-run-time.md): when the questions, options or keys come from data.
- [Question types](question-types.md): what each answer holds.
- [Patterns](patterns/index.md): complete examples that put the answers to work.
- [The client and its errors](client-and-errors.md): the options, retries, and every `DecisionError` a call can return.
