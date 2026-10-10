using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZeroAlloc.Results;

namespace Minos.Tests;

public sealed record TicketContext(string Subject, string Body);

[JsonSerializable(typeof(TicketContext))]
internal sealed partial class TicketContextJsonContext : JsonSerializerContext;

[Questions(State = typeof(TicketContext))]
public partial record TicketUrgency
{
    [Noul("Does the ticket convey urgency?")]
    public partial Noul IsUrgent { get; }
}

/// <summary>Covers the typed <see cref="DecisionClientExtensions"/> overloads over a fake <see cref="IDecisionClient"/>.</summary>
public sealed class TypedEvaluationDefaultTests
{
    [Fact]
    public async Task String_ReturnsTypedAnswers()
    {
        IDecisionClient client = Returning("response-noul.json");

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        Assert.True(result.Value.IsUrgent.Value);
    }

    [Fact]
    public async Task String_SendsTheGeneratedDefinitionAndTheState_WithoutAModel()
    {
        var fake = Returning("response-noul.json");

        await fake.EvaluateAsync<UrgencyCheck>("text");

        var request = fake.OnlyRequest();
        Assert.True(request.State.TryGetString(out var state));
        Assert.Equal("text", state);
        Assert.Null(request.Model);
        Assert.Same(UrgencyCheck.Definition, request.Definition);
    }

    [Fact]
    public async Task CancellationToken_IsForwarded()
    {
        var fake = Returning("response-noul.json");
        using var cts = new CancellationTokenSource();

        await fake.EvaluateAsync<UrgencyCheck>("text", cts.Token);

        Assert.Equal(cts.Token, fake.LastToken);
    }

    [Fact]
    public async Task JsonElement_And_Utf8_SendTheSameState()
    {
        const string json = """{"messages":[{"role":"user","content":"Help!"}]}""";
        var fromElement = Returning("response-noul.json");
        var fromUtf8 = Returning("response-noul.json");
        using var document = JsonDocument.Parse(json);

        var elementResult = await fromElement.EvaluateAsync<UrgencyCheck>(document.RootElement);
        var utf8Result = await fromUtf8.EvaluateUtf8Async<UrgencyCheck>(Encoding.UTF8.GetBytes(json));

        Assert.True(elementResult.IsSuccess);
        Assert.True(utf8Result.IsSuccess);
        Assert.Equal(elementResult.Value, utf8Result.Value);
        Assert.True(fromElement.OnlyRequest().State.TryGetJson(out var elementState));
        Assert.True(fromUtf8.OnlyRequest().State.TryGetJson(out var utf8State));
        Assert.True(JsonElement.DeepEquals(elementState, utf8State));
        Assert.True(JsonElement.DeepEquals(document.RootElement, utf8State));
        Assert.Same(UrgencyCheck.Definition, fromUtf8.OnlyRequest().Definition);
    }

    [Fact]
    public async Task JsonStringState_IsSentAsText()
    {
        var fake = Returning("response-noul.json");

        await fake.EvaluateUtf8Async<UrgencyCheck>("\"plain text\""u8.ToArray());

        Assert.True(fake.OnlyRequest().State.TryGetString(out var state));
        Assert.Equal("plain text", state);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("1 2")]
    [InlineData("{} {}")]
    [InlineData("  ")]
    public void Utf8_ThatIsNotASingleJsonValue_ThrowsSynchronously_WithoutCallingTheClient(string json)
    {
        var fake = Returning("response-noul.json");

        var exception = ClientTestKit.ThrowsSynchronously<ArgumentException>(
            () => fake.EvaluateUtf8Async<UrgencyCheck>(Encoding.UTF8.GetBytes(json)).AsTask());

        Assert.Equal("utf8JsonState", exception.ParamName);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void Utf8_NumberState_ThrowsSynchronously_WithoutCallingTheClient()
    {
        var fake = Returning("response-noul.json");

        var exception = ClientTestKit.ThrowsSynchronously<ArgumentException>(
            () => fake.EvaluateUtf8Async<UrgencyCheck>("42"u8.ToArray()).AsTask());

        Assert.Equal("utf8JsonState", exception.ParamName);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void NullString_ThrowsSynchronously()
    {
        var fake = Returning("response-noul.json");

        Assert.Equal(
            "state",
            ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync<UrgencyCheck>((string)null!).AsTask()).ParamName);

        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    public void JsonElement_OfAnotherKind_ThrowsSynchronously(string json)
    {
        var fake = Returning("response-noul.json");
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            "state",
            ClientTestKit.ThrowsSynchronously<ArgumentException>(() => fake.EvaluateAsync<UrgencyCheck>(document.RootElement).AsTask()).ParamName);

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void UndefinedJsonElement_ThrowsSynchronously()
    {
        var fake = Returning("response-noul.json");

        ClientTestKit.ThrowsSynchronously<ArgumentException>(() => fake.EvaluateAsync<UrgencyCheck>(default(JsonElement)).AsTask());

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task TypedState_IsSerializedThroughItsTypeInfo()
    {
        var fake = Returning("response-noul.json");
        var ticket = new TicketContext("Payouts failing", "Help! My payouts have been failing for 3 days.");

        var result = await fake.EvaluateAsync<TicketUrgency, TicketContext>(ticket, TicketContextJsonContext.Default.TicketContext);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        var request = fake.OnlyRequest();
        Assert.True(request.State.TryGetJson(out var state));
        using var expected = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(ticket, TicketContextJsonContext.Default.TicketContext));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, state));
        Assert.Null(request.Model);
        Assert.Same(TicketUrgency.Definition, request.Definition);
    }

    [Fact]
    public void TypedState_InvalidArguments_ThrowSynchronously()
    {
        var fake = Returning("response-noul.json");
        var ticket = new TicketContext("s", "b");

        Assert.Equal(
            "state",
            ClientTestKit.ThrowsSynchronously<ArgumentNullException>(
                () => fake.EvaluateAsync<TicketUrgency, TicketContext>(null!, TicketContextJsonContext.Default.TicketContext).AsTask()).ParamName);
        Assert.Equal(
            "stateTypeInfo",
            ClientTestKit.ThrowsSynchronously<ArgumentNullException>(
                () => fake.EvaluateAsync<TicketUrgency, TicketContext>(ticket, null!).AsTask()).ParamName);
        Assert.Equal(
            "state",
            ClientTestKit.ThrowsSynchronously<ArgumentException>(
                () => fake.EvaluateAsync<NumericUrgency, NumericState>(new NumericState(42), NumericStateJsonContext.Default.NumericState).AsTask()).ParamName);

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task ChoiceAndScore_ReturnTypedAnswers()
    {
        var routing = await Returning("response-choice.json").EvaluateAsync<DepartmentRouting>("text");
        var frustration = await Returning("response-score.json").EvaluateAsync<FrustrationCheck>("text");

        Assert.Equal(Department.Billing, routing.Value.Department.Value);
        Assert.Equal(0.88, routing.Value.Department.Probabilities[Department.Billing]);
        Assert.Equal(Frustration.Frustrated, frustration.Value.Frustration.Value);
        Assert.Equal(0.92, frustration.Value.Frustration.Confidence);
    }

    [Fact]
    public async Task FailedResponse_ReturnsTheSameError()
    {
        var error = new DecisionError(DecisionErrorKind.RateLimited, "Slow down.") { StatusCode = 429 };
        IDecisionClient client = new ClientTestKit.CapturingClient(failure: error);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task AnAsynchronousAnswer_IsMappedWhenItCompletes()
    {
        using var client = new YieldingClient();
        var set = QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out var urgent).Build().Value;

        var typed = await client.EvaluateAsync<UrgencyCheck>("text");
        var builtSet = await client.EvaluateAsync(set, "text");

        Assert.Equal(0.95, typed.Value.IsUrgent.Probability);
        Assert.Equal(0.95, builtSet.Value.Get(urgent).Probability);
    }

    private static ClientTestKit.CapturingClient Returning(string fixture) => new(responseJson: Fixture.Text(fixture));

    /// <summary>Answers after a yield, so the extensions take their asynchronous path.</summary>
    private sealed class YieldingClient : IDecisionClient
    {
        public async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return Result<DecisionResponse, DecisionError>.Success(ClientTestKit.CapturingClient.Canned(request.Definition));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
