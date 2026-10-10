using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Minos.Serialization;
using Minos.Transport;
using ZeroAlloc.Results;
using static Minos.Tests.ClientTestKit;

namespace Minos.Tests;

/// <summary>A state that serializes to a JSON number, which Jev does not accept.</summary>
[JsonConverter(typeof(NumericStateConverter))]
public readonly record struct NumericState(int Value);

internal sealed class NumericStateConverter : JsonConverter<NumericState>
{
    public override NumericState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, NumericState value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);
}

[JsonSerializable(typeof(NumericState))]
internal sealed partial class NumericStateJsonContext : JsonSerializerContext;

[Questions(State = typeof(NumericState))]
public partial record NumericUrgency
{
    [Noul("Is the number urgent?")]
    public partial Noul IsUrgent { get; }
}

/// <summary>A state whose converter writes a raw value with leading JSON whitespace, which Jev accepts.</summary>
[JsonConverter(typeof(PaddedStateConverter))]
public sealed record PaddedState(string Json);

internal sealed class PaddedStateConverter : JsonConverter<PaddedState>
{
    public override PaddedState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, PaddedState value, JsonSerializerOptions options)
        => writer.WriteRawValue(value.Json);
}

[JsonSerializable(typeof(PaddedState))]
internal sealed partial class PaddedStateJsonContext : JsonSerializerContext;

[Questions(State = typeof(PaddedState))]
public partial record PaddedUrgency
{
    [Noul("Is it urgent?")]
    public partial Noul IsUrgent { get; }
}

/// <summary>Covers <see cref="DecisionClient"/>'s own typed <c>EvaluateAsync</c> overloads, the raw UTF-8 path.</summary>
public sealed class DecisionClientTypedTests : IDisposable
{
    private const string TicketJson = """{"messages":[{"role":"user","content":"Help!"}]}""";

    private static readonly TicketContext Ticket = new("Payouts failing", "Help! My payouts have been failing for 3 days.");

    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public Task String_SendsTheDefaultPathsRequest()
        => AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<UrgencyCheck>("text"),
            c => c.EvaluateAsync<UrgencyCheck>("text"));

    [Fact]
    public Task String_WithToken_SendsTheDefaultPathsRequest()
        => AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<UrgencyCheck>("text with \"quotes\" and é", CancellationToken.None),
            c => c.EvaluateAsync<UrgencyCheck>("text with \"quotes\" and é", CancellationToken.None));

    [Fact]
    public async Task JsonElement_SendsTheDefaultPathsRequest()
    {
        using var document = JsonDocument.Parse(TicketJson);
        await AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<UrgencyCheck>(document.RootElement),
            c => c.EvaluateAsync<UrgencyCheck>(document.RootElement));
    }

    [Fact]
    public async Task JsonElement_WithToken_SendsTheDefaultPathsRequest()
    {
        using var document = JsonDocument.Parse("""[1, "two", {"three": 3}]""");
        await AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<UrgencyCheck>(document.RootElement, CancellationToken.None),
            c => c.EvaluateAsync<UrgencyCheck>(document.RootElement, CancellationToken.None));
    }

    [Fact]
    public async Task JsonElementString_SendsTheDefaultPathsRequest()
    {
        using var document = JsonDocument.Parse("\"plain text\"");
        await AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<UrgencyCheck>(document.RootElement),
            c => c.EvaluateAsync<UrgencyCheck>(document.RootElement));
    }

    [Fact]
    public Task LargeState_GrowsTheBody_AndSendsTheDefaultPathsRequest()
    {
        var state = string.Concat(Enumerable.Repeat("Help! \"Urgent\" é 😀 ", 10_000));
        return AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<UrgencyCheck>(state),
            c => c.EvaluateAsync<UrgencyCheck>(state));
    }

    [Fact]
    public Task Utf8_SendsTheDefaultPathsRequest()
    {
        var utf8 = Encoding.UTF8.GetBytes(TicketJson);
        return AssertSendsTheDefaultRequest(
            c => c.EvaluateUtf8Async<UrgencyCheck>(utf8),
            c => c.EvaluateUtf8Async<UrgencyCheck>(utf8));
    }

    [Fact]
    public Task Utf8String_SendsTheDefaultPathsRequest()
    {
        var utf8 = "  \"plain text\"  "u8.ToArray();
        return AssertSendsTheDefaultRequest(
            c => c.EvaluateUtf8Async<UrgencyCheck>(utf8, CancellationToken.None),
            c => c.EvaluateUtf8Async<UrgencyCheck>(utf8, CancellationToken.None));
    }

    [Fact]
    public Task TypedState_SendsTheDefaultPathsRequest()
        => AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<TicketUrgency, TicketContext>(Ticket, TicketContextJsonContext.Default.TicketContext),
            c => c.EvaluateAsync<TicketUrgency, TicketContext>(Ticket, TicketContextJsonContext.Default.TicketContext));

    [Fact]
    public Task TypedState_WithToken_SendsTheDefaultPathsRequest()
        => AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<TicketUrgency, TicketContext>(Ticket, TicketContextJsonContext.Default.TicketContext, CancellationToken.None),
            c => c.EvaluateAsync<TicketUrgency, TicketContext>(Ticket, TicketContextJsonContext.Default.TicketContext, CancellationToken.None));

    [Theory]
    [InlineData(" \t\r\n{\"messages\":[]}")]
    [InlineData("\n  [1, 2]")]
    [InlineData("  \"plain text\"")]
    public Task TypedStateWithLeadingWhitespace_SendsTheDefaultPathsRequest(string json)
    {
        var state = new PaddedState(json);
        return AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<PaddedUrgency, PaddedState>(state, PaddedStateJsonContext.Default.PaddedState),
            c => c.EvaluateAsync<PaddedUrgency, PaddedState>(state, PaddedStateJsonContext.Default.PaddedState));
    }

    [Fact]
    public void TypedStateWithLeadingWhitespace_ThatIsANumber_Throws()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        var pool = new CountingPool();
        using var client = Client(handler, pool);

        var exception = ThrowsSynchronously<ArgumentException>(
            () => client.EvaluateAsync<PaddedUrgency, PaddedState>(new PaddedState(" \n 42"), PaddedStateJsonContext.Default.PaddedState).AsTask());

        Assert.Equal("state", exception.ParamName);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public Task EscapedQuestions_SendTheDefaultPathsRequest()
        => AssertSendsTheDefaultRequest(
            c => c.EvaluateAsync<EdgeCases>("text"),
            c => c.EvaluateAsync<EdgeCases>("text"),
            respondWith: null);

    [Fact]
    public async Task Response_IsParsed()
    {
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")), pool);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        Assert.True(result.Value.IsUrgent.Value);
        Assert.Equal(0, pool.Outstanding);
    }

    [Theory]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"model":"m","usage":{"input_tokens":1,"output_tokens":1},"extra":[1,{"answers":null}]}""")]
    [InlineData("""{"model":"m","extra":{"answers":{}},"answers":{"is_urgent":{"noul":0.3,"type":"noul"}},"usage":{"input_tokens":1,"output_tokens":1}}""")]
    [InlineData("""{"id":"gen-1","model":"m","usage":{"input_tokens":1,"output_tokens":1},"answers":{"is_urgent":{"type":"noul","noul":0.3}}}""")]
    public async Task Response_WithExtraFieldsAndAnswersAnywhere_IsParsed(string body)
    {
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, body));

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.3, result.Value.IsUrgent.Probability);
    }

    [Fact]
    public async Task Response_LargerThanTheInitialBuffer_IsParsed()
    {
        var padding = new string('x', 100_000);
        var body = "{\"padding\":\"" + padding + "\"," + Fixture.Text("response-noul.json").TrimStart()[1..];
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, body), pool);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        Assert.Equal(0, pool.Outstanding);
    }

    [Theory]
    [InlineData("""{"model":"m","usage":{"input_tokens":1,"output_tokens":1}}""")]
    [InlineData("""{"answers":null}""")]
    [InlineData("[]")]
    [InlineData("")]
    public async Task Response_WithoutAnswers_IsInvalidResponse(string body)
    {
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, body), pool);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task AnswerOfTheWrongType_IsInvalidResponse_WithTheJsonException()
    {
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-choice.json")), pool);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.IsType<JsonException>(result.Error.Exception);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task ScoreAnswerWithoutLegend_IsInvalidResponse()
    {
        var pool = new CountingPool();
        var body = Fixture.Text("response-score.json").Replace(
            "\"legend\": { \"0\": \"Calm\", \"1\": \"Frustrated\", \"2\": \"Very angry\" },", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("legend", body, StringComparison.Ordinal);
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, body), pool);

        var result = await client.EvaluateAsync<FrustrationCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.IsType<JsonException>(result.Error.Exception);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task MalformedResponse_IsInvalidResponse()
    {
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, "not json"), pool);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.IsAssignableFrom<JsonException>(result.Error.Exception);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task Status401_IsUnauthorized()
    {
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.Unauthorized, """{"detail":"bad key"}"""), pool);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unauthorized, result.Error.Kind);
        Assert.Equal(401, result.Error.StatusCode);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task Request_UsesBearerAuthAndTheSystemOneEndpoint()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = Client(handler);

        _ = await client.EvaluateAsync<UrgencyCheck>("text");

        var sent = OnlyRequest(handler);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri("https://api.typesafe.ai/v1/systemone"), sent.Uri);
        Assert.Equal("Bearer test-key", sent.Authorization);
    }

    [Fact]
    public async Task OpenRouter_UsesTheApiPrefix()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-openrouter.json"));
        using var client = Client(handler, provider: DecisionProvider.OpenRouter);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/systemone"), OnlyRequest(handler).Uri);
    }

    [Fact]
    public async Task CallerCancellation_Throws_AndReturnsTheBuffers()
    {
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")), pool);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.EvaluateAsync<UrgencyCheck>("text", cancellation.Token));

        Assert.True(pool.Rented > 0);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task DisposedClient_Throws()
    {
        var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")));
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync<UrgencyCheck>("text"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateUtf8Async<UrgencyCheck>("{}"u8.ToArray()));
        using var document = JsonDocument.Parse("{}");
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync<UrgencyCheck>(document.RootElement));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await client.EvaluateAsync<UrgencyCheck>(document.RootElement, CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await client.EvaluateAsync<TicketUrgency, TicketContext>(Ticket, TicketContextJsonContext.Default.TicketContext));
    }

    [Fact]
    public void DisposedClient_WithInvalidArguments_ThrowsForTheArgument_NotObjectDisposedException()
    {
        var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")));
        client.Dispose();

        ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync<UrgencyCheck>((string)null!).AsTask());
        ThrowsSynchronously<ArgumentException>(() => client.EvaluateAsync<UrgencyCheck>(default(JsonElement)).AsTask());
        ThrowsSynchronously<ArgumentException>(() => client.EvaluateUtf8Async<UrgencyCheck>("not json"u8.ToArray()).AsTask());
        ThrowsSynchronously<ArgumentNullException>(
            () => client.EvaluateAsync<TicketUrgency, TicketContext>(null!, TicketContextJsonContext.Default.TicketContext).AsTask());
        ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync<TicketUrgency, TicketContext>(Ticket, null!).AsTask());
    }

    [Fact]
    public void InvalidArguments_ThrowSynchronously_WithoutARequest()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        var pool = new CountingPool();
        using var client = Client(handler, pool);

        ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync<UrgencyCheck>((string)null!).AsTask());
        ThrowsSynchronously<ArgumentException>(() => client.EvaluateAsync<UrgencyCheck>(default(JsonElement)).AsTask());
        using (var number = JsonDocument.Parse("42"))
        {
            Assert.Equal("state", ThrowsSynchronously<ArgumentException>(() => client.EvaluateAsync<UrgencyCheck>(number.RootElement).AsTask()).ParamName);
        }

        foreach (var json in new[] { "{", "", "1 2", "{} {}", "  ", "42", "true", "null" })
        {
            var exception = ThrowsSynchronously<ArgumentException>(() => client.EvaluateUtf8Async<UrgencyCheck>(Encoding.UTF8.GetBytes(json)).AsTask());
            Assert.Equal("utf8JsonState", exception.ParamName);
        }

        ThrowsSynchronously<ArgumentNullException>(
            () => client.EvaluateAsync<TicketUrgency, TicketContext>(null!, TicketContextJsonContext.Default.TicketContext).AsTask());
        ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync<TicketUrgency, TicketContext>(Ticket, null!).AsTask());
        Assert.Equal(
            "state",
            ThrowsSynchronously<ArgumentException>(
                () => client.EvaluateAsync<NumericUrgency, NumericState>(new NumericState(42), NumericStateJsonContext.Default.NumericState).AsTask()).ParamName);

        Assert.Empty(handler.Requests);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task TheExtension_RejectsANumericTypedStateToo()
    {
        IDecisionClient fake = new CapturingClient();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await fake.EvaluateAsync<NumericUrgency, NumericState>(new NumericState(42), NumericStateJsonContext.Default.NumericState));
    }

    private async Task AssertSendsTheDefaultRequest<T>(
        Func<IDecisionClient, ValueTask<Result<T, DecisionError>>> viaDefaultPath,
        Func<DecisionClient, ValueTask<Result<T, DecisionError>>> viaDecisionClient,
        string? respondWith = "response-noul.json")
        where T : IQuestionSet<T>
    {
        var fake = new CapturingClient(responseJson: respondWith is null ? "{}" : Fixture.Text(respondWith));
        var expectedResult = await viaDefaultPath(fake);
        var captured = fake.OnlyRequest();
        Assert.Same(T.Definition, captured.Definition);
        Assert.Null(captured.Model);
        var expected = ExpectedBody(captured);

        var handler = StubHandler.Json(HttpStatusCode.OK, respondWith is null ? "{}" : Fixture.Text(respondWith));
        var pool = new CountingPool();
        using var client = Client(handler, pool);

        var result = await viaDecisionClient(client);

        var sent = JsonNode.Parse(OnlyRequest(handler).Body!);
        Assert.True(JsonNode.DeepEquals(expected, sent), sent?.ToJsonString());
        if (respondWith is not null)
        {
            Assert.True(expectedResult.IsSuccess);
            Assert.True(result.IsSuccess);
            Assert.Equal(expectedResult.Value, result.Value);
        }

        // The raw path rents its buffers from the client's pool; the default path would not touch it.
        Assert.True(pool.Rented >= 2, $"rented {pool.Rented}");
        Assert.Equal(0, pool.Outstanding);
    }

    private DecisionClient Client(StubHandler handler, CountingPool? pool = null, DecisionProvider provider = DecisionProvider.TypeSafe)
        => ClientTestKit.Client(_httpClients, handler, pool, provider);
}
