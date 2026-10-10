---
id: testing-your-code
title: Testing your code
sidebar_position: 10
description: Test code that calls Jev with a fake IDecisionClient or a real DecisionClient over a canned HTTP reply, and pin each decision.
---

# Testing your code

A test of code that uses Jev should not call Jev. A real call costs money, answers a little differently from one run to
the next, and needs a key and a network. What you want to test is your own logic: given these answers, does the code
decide correctly, and given a failure, does it cope? Both are easy to test if you hand the code **canned answers**: a
fixed reply, written in the test.

This page shows the two ways to do that, and how to use them to pin each decision your code makes. The examples use
xUnit, but nothing here depends on it.

## The code under test

The examples test one small class, a ticket triager. It asks Jev two questions about a ticket's text and turns the
answers into one of three routes: escalate it, put it in a queue, or send it to a person to review.

<!-- snippet: TestingYourCode_Triager -->
```cs
using Minos;

public enum TriageDesk
{
    Billing,
    Technical,
    ProductTeam,
}

// Two questions about a ticket's text. The wire keys are the property names in snake_case: is_urgent and desk.
[Questions]
public partial record TriageQuestions
{
    [Noul("Does this ticket need help right away?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which desk should handle this ticket?")]
    public partial Choice<TriageDesk> Desk { get; }
}

public enum TriageRoute
{
    Escalate,
    Queue,
    Review,
}

// The class under test. It asks for an IDecisionClient, so a test can hand it any implementation.
public sealed class TicketTriager(IDecisionClient client)
{
    public async Task<TriageRoute> RouteAsync(string ticketText, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<TriageQuestions>(ticketText, cancellationToken);
        if (result.IsFailure)
        {
            // Jev could not answer, so a person looks at the ticket.
            return TriageRoute.Review;
        }

        var answers = result.Value;
        if (answers.IsUrgent.Value)
        {
            return TriageRoute.Escalate;
        }

        // An unsure desk pick is not acted on.
        return answers.Desk.Confidence < 0.6 ? TriageRoute.Review : TriageRoute.Queue;
    }
}
```
<!-- endSnippet -->

Two things make the class easy to test. It asks for an `IDecisionClient` in its constructor and does not create a client
itself, so a test can hand it any implementation. And it turns a failed call into a decision, the review, instead of
throwing, which is the behaviour a test most wants to pin. [Dependency injection](dependency-injection.md) explains how
a host supplies the real client. A test does not need a container: it builds the class directly, as the tests below do.

## Way one: a fake `IDecisionClient`

`IDecisionClient` is the library's interface for calling Jev, and `DecisionClient` implements it. A **fake** is a small class of
your own that implements it and returns a fixed reply, with no HTTP at all.

A fake implements only two members:

- `EvaluateAsync(SystemOneRequest, CancellationToken)`, which takes the request and returns a `Result` of a response or
  a [`DecisionError`](client-and-errors.md#errors);
- `ListModelsAsync`, which your code probably never calls, so the fake can throw.

Every other member of the interface, such as the typed `EvaluateAsync<T>` calls your code makes, has a default
implementation that builds the request and calls the first of those two. So the typed call in the triager, and the
text, `JsonElement` and UTF-8 overloads, all end up in the one method the fake wrote.

<!-- snippet: TestingYourCode_Fake -->
```cs
using Minos;
using ZeroAlloc.Results;

// A fake implements EvaluateAsync, GetService and Dispose. The typed calls are extension methods over EvaluateAsync.
public sealed class FakeDecision(Result<DecisionResponse, DecisionError> reply) : IDecisionClient
{
    private readonly List<DecisionRequest> _requests = [];

    // Every request the code under test sent, so a test can check what was asked.
    public IReadOnlyList<DecisionRequest> Requests => _requests;

    // A reply that answers both questions of TriageQuestions, in the order the set declares them.
    public static FakeDecision Answering(double urgent, TriageDesk desk, double deskConfidence)
    {
        // A Choice answer gives one probability per option, in the order the enum declares them.
        var desks = Enum.GetValues<TriageDesk>();
        var probabilities = new double[desks.Length];
        for (var i = 0; i < desks.Length; i++)
        {
            probabilities[i] = desks[i] == desk ? 0.7 : 0.15;
        }

        return new FakeDecision(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
            TriageQuestions.Definition,
            [QuestionAnswer.Noul(urgent), QuestionAnswer.Choice(Array.IndexOf(desks, desk), deskConfidence, probabilities)],
            model: "fake")));
    }

    // A reply that is a failure, as a rejected key or a network error would be.
    public static FakeDecision Failing(DecisionErrorKind kind)
        => new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, "The fake failed on purpose.")));

    // A busy service: the kind and message are the constructor's, the rest are init properties.
    public static FakeDecision Overloaded(TimeSpan retryAfter)
        => new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Overloaded, "The fake is busy on purpose.")
        {
            StatusCode = 503,
            RetryAfter = retryAfter,
        }));

    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Add(request);
        return ValueTask.FromResult(reply);
    }

    // The fake offers no services, such as DecisionClientMetadata.
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
```
<!-- endSnippet -->

The reply is a `SystemOneResponse`, and its answers are keyed by **question key**: the property name in snake_case, so
`IsUrgent` is `is_urgent`, unless the attribute sets `Key`.
[Typed evaluation](typed-evaluation.md#declaring-the-questions) has the rule. Each answer is a `NoulAnswer`, a
`ChoiceAnswer` or a `ScoreAnswer`, and [Question types](question-types.md) says what each one holds. A Choice answer
names its option by the enum member in snake_case, so `TriageDesk.Billing` is `billing` and `TriageDesk.ProductTeam` is
`product_team`. The fake gets the keys from `JsonNamingPolicy.SnakeCaseLower`, which converts names the way the
library does.

With the fake in hand, a test is a few lines: build the class, call it, assert the decision. The fake also keeps every
request, so the test can check what the code asked.

<!-- snippet: TestingYourCode_FakeTests -->
```cs
[Fact]
public async Task AnUrgentTicket_IsEscalated_AndTheTicketTextIsWhatWasAsked()
{
    var client = FakeDecision.Answering(urgent: 0.92, TriageDesk.Billing, deskConfidence: 0.8);
    var triager = new TicketTriager(client);

    var route = await triager.RouteAsync("Payouts have been failing for 3 days.", CancellationToken.None);

    Assert.Equal(TriageRoute.Escalate, route);
    Assert.Collection(client.Requests, request =>
    {
        Assert.True(request.State.TryGetString(out var state));
        Assert.Equal("Payouts have been failing for 3 days.", state);
    });
}

[Fact]
public async Task WhenDecisionFails_ThePersonReviews()
{
    var triager = new TicketTriager(FakeDecision.Failing(DecisionErrorKind.Network));

    var route = await triager.RouteAsync("Any ticket.", CancellationToken.None);

    Assert.Equal(TriageRoute.Review, route);
}
```
<!-- endSnippet -->

If you use a mocking library instead of a hand-written fake, mind the default methods. A mock intercepts them like any
other member, so set up the overload your code calls, here `EvaluateAsync<T>(string, CancellationToken)`, or enable
`CallBase` so that the mock runs the real default. A hand-written fake avoids the question, because the defaults do
the routing.

## Pin each decision

Your code turns an answer into a decision with a rule: a cut-off, a confidence gate, a fallback. Those rules are what
the tests are for, so write one row per rule, including the exact edge of each cut-off.

<!-- snippet: TestingYourCode_Pinning -->
```cs
// Each row is one canned answer and the decision the code must make from it, including the cut-offs themselves.
[Theory]
[InlineData(0.50, 0.9, TriageRoute.Escalate)] // a Noul is true at 0.5 or more
[InlineData(0.49, 0.9, TriageRoute.Queue)]
[InlineData(0.10, 0.60, TriageRoute.Queue)] // the desk is trusted at 0.6 or more
[InlineData(0.10, 0.59, TriageRoute.Review)]
public async Task EachAnswer_PinsOneDecision(double urgent, double deskConfidence, TriageRoute expected)
{
    var triager = new TicketTriager(FakeDecision.Answering(urgent, TriageDesk.Technical, deskConfidence));

    Assert.Equal(expected, await triager.RouteAsync("A ticket.", CancellationToken.None));
}
```
<!-- endSnippet -->

The first two rows pin the edge of a Noul. A Noul's `Value` is true at a probability of 0.5 or more, so 0.50 escalates
and 0.49 does not. The next two pin the triager's own gate on a Choice, which trusts the desk at a confidence of 0.6 or
more. When someone changes a threshold, one row fails, and its values say which decision moved. For more on choosing
such thresholds, see [confidence routing](patterns/confidence-routing.md).

Add a row for each failure you care about as well. The fake's `Failing` method returns an error of any
[`DecisionErrorKind`](client-and-errors.md#the-kinds), which is how the test above shows that a network error ends in a
review. `Overloaded` shows how to give an error more detail: the constructor takes the kind and the message, and the
example sets the `StatusCode` and `RetryAfter` init properties. `Detail` and `Exception` are set the same way when
the failure has them.

## Way two: a real `DecisionClient` over a canned HTTP reply

A fake never runs `DecisionClient`. The client replaces the interface's default typed calls with its own request writer
and its own parser for the answers, and the defaults send the default model. So a fake cannot tell you that your
question set builds the request you meant, or that a reply parses into the answers you read. For that, run a real
`DecisionClient` and replace only the network. A `DecisionClient` can take an `HttpClient` you made, and an `HttpClient` can
take a **message handler**: the object that actually sends the request. A handler of your own that answers from memory
means the full client runs, and no packet leaves the machine.

<!-- snippet: TestingYourCode_Handler -->
```cs
using System.Net;
using System.Text;

// Answers every request with one canned status and body, and keeps the bodies it was sent.
public sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    private readonly List<string> _requestBodies = [];

    public IReadOnlyList<string> RequestBodies => _requestBodies;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
```
<!-- endSnippet -->

The canned body is what Jev would send back. For the two questions of the triager it looks like this.

<!-- snippet: TestingYourCode_Body -->
```cs
// What Jev sends back. The keys under "answers" are the question keys: the property names in snake_case.
private const string UrgentBody = """
    {
      "model": "jev-1.13.0",
      "answers": {
        "is_urgent": { "type": "noul", "noul": 0.92 },
        "desk": {
          "type": "choice",
          "choice": "billing",
          "probabilities": { "billing": 0.7, "technical": 0.2, "product_team": 0.1 },
          "confidence": 0.8
        }
      },
      "usage": { "input_tokens": 40, "output_tokens": 6 }
    }
    """;
```
<!-- endSnippet -->

The parts of the body that matter are these.

- **`answers`** holds one entry per question, under the question key, the same snake_case name the fake used.
- **`type`** names the kind of answer: `noul`, `choice` or `score`. An answer without it is rejected.
- **`noul`** is a Noul's probability. A Choice has `choice`, the option's key, `probabilities` for the options and a
  `confidence`. A Score has `score`, a `legend`, `probabilities` and a `confidence`.
- **`model` and `usage`** are what the real service adds. A typed call reads only `answers`, so a canned body for one
  can leave them out.

A body that leaves out a field the typed call needs, such as a Choice's `confidence`, comes back as a failed `Result`
with the kind `InvalidResponse`, not as an exception. So a typo in a canned body shows up as a failing test, which is
what you want. [Typed evaluation](typed-evaluation.md) has the rest of the wire shape.

The tests then build the client over the handler.

<!-- snippet: TestingYourCode_HttpTests -->
```cs
[Fact]
public async Task ARealClient_SendsTheQuestions_AndReadsTheCannedAnswers()
{
    var handler = new CannedHandler(HttpStatusCode.OK, UrgentBody);
    using var http = new HttpClient(handler);
    // A dummy key passes validation, and no retries means a failing reply is returned at once.
    using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "test-key", MaxRetries = 0 });

    var route = await new TicketTriager(client).RouteAsync("Payouts have been failing for 3 days.", CancellationToken.None);

    Assert.Equal(TriageRoute.Escalate, route);
    Assert.Collection(handler.RequestBodies, sent =>
    {
        using var request = System.Text.Json.JsonDocument.Parse(sent);
        Assert.Equal("Payouts have been failing for 3 days.", request.RootElement.GetProperty("state").GetString());
        Assert.True(request.RootElement.GetProperty("questions").TryGetProperty("is_urgent", out _));
    });
}

[Fact]
public async Task ARejectedKey_BecomesAFailure_AndThePersonReviews()
{
    var handler = new CannedHandler(HttpStatusCode.Unauthorized, """{"error":"Invalid API key"}""");
    using var http = new HttpClient(handler);
    using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "test-key", MaxRetries = 0 });

    var result = await client.EvaluateAsync<TriageQuestions>("Any ticket.", CancellationToken.None);

    Assert.True(result.IsFailure);
    Assert.Equal(DecisionErrorKind.Unauthorized, result.Error.Kind);
    Assert.Equal(TriageRoute.Review, await new TicketTriager(client).RouteAsync("Any ticket.", CancellationToken.None));
}
```
<!-- endSnippet -->

A few details are worth knowing.

- **Give the client a key.** Without `ApiKey`, the client reads the provider's key variable, `TYPESAFE_API_KEY` or
  `OPENROUTER_API_KEY`, and throws `InvalidOperationException` when that is unset, as it is on most CI machines. A dummy
  key keeps the test independent of the environment.
- **Leave out the base address.** A client built over an `HttpClient` with no base address sets its own, and the handler
  never uses it.
- **Set `MaxRetries` to 0 when a test replies with an error.** The client retries a 429, a 5xx and a network failure
  with a growing wait, as [Retries](client-and-errors.md#retries) describes, so an error reply under the defaults makes
  three attempts and slows the test. A 401, as in the second test, is never retried.
- **The handler sees the real request.** The first test reads the body it was sent, and checks the state and a question
  key. That is a check a fake cannot make.

To test retries themselves, make the handler follow a script, with one reply per attempt, such as a 503 and then a
success, and assert how many requests it saw. Set `InitialBackoff` and `MaxRetryDelay` in the options to a few
milliseconds, such as 1 ms and 5 ms, so that the waits between attempts cost the test nothing.

## Which to use

Use a fake for most tests. It is short, fast, and makes the answers the point of the test. Add a few tests with a real
client and a canned handler for what a fake skips: the request on the wire, the parsing of a reply, and how the client
reports each kind of failure. [Logging, traces and metrics](observability.md) shows how to read what a client reports
in a test.

## Why a test should never call the real API

- **It costs money.** Every call is billed, and a test suite makes many calls on every run, on every machine.
- **It is not repeatable.** The model's answers can differ from one run to the next, so a test that passes today can
  fail tomorrow without a code change. A test of your logic should fail only when your logic changes.
- **It needs a key.** A key in a test run is a key in your CI settings and on each developer's machine.
- **It is slow, and it can fail for reasons that are not yours.** A network blip, a rate limit or an outage fails your
  build.
- **It cannot reach the cases that matter.** A canned reply can be a 401, a 503, an unreadable body or a borderline
  probability on demand. The real service gives you those only by chance.

A check against the real API still has a place: a small, optional test that you run by hand, to confirm that a question
set is understood. Keep it out of the normal run, and have it skip unless someone opts in. This repository's own live
tests are skipped unless `MINOS_LIVE=1` and an API key are set, because their calls are billed.

## Next

- [Patterns](patterns/index.md): four ways to use the answers, with examples that run as tests against canned answers.
- [Samples](samples.md): three runnable cookbook samples that replay recorded answers instead of calling the API.
- [The client and its errors](client-and-errors.md): the options, and every `DecisionError` a test can provoke.
