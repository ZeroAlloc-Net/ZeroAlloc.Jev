using System.Text;
using System.Text.Json;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace Minos.Tests;

/// <summary>Covers <see cref="DecisionClientExtensions"/> over a fake <see cref="IDecisionClient"/>.</summary>
public sealed class DecisionClientExtensionsTests
{
    [Fact]
    public async Task Typed_call_builds_the_request_from_the_definition_and_creates_the_answers()
    {
        var fake = new ClientTestKit.CapturingClient();

        var result = await fake.EvaluateAsync<UrgencyCheck>("a ticket");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        var request = fake.OnlyRequest();
        Assert.Same(UrgencyCheck.Definition, request.Definition);
        Assert.Equal(DecisionContent.FromString("a ticket"), request.State);
        Assert.Null(request.Model);
        Assert.Equal(0, request.RetryAttempt);
    }

    [Fact]
    public async Task Built_set_call_returns_answers_read_through_handles()
    {
        var builder = QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out var urgent);
        var set = builder.Build().Value;
        var fake = new ClientTestKit.CapturingClient(set.Definition);

        var result = await fake.EvaluateAsync(set, "a ticket");

        Assert.Equal(0.95, result.Value.Get(urgent).Probability);
        Assert.Same(set.Definition, fake.OnlyRequest().Definition);
    }

    [Fact]
    public async Task A_failure_passes_through()
    {
        var error = new DecisionError(DecisionErrorKind.Server, "boom");
        var fake = new ClientTestKit.CapturingClient(failure: error);

        var typed = await fake.EvaluateAsync<UrgencyCheck>("s");
        var builtSet = await fake.EvaluateAsync(QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out _).Build().Value, "s");

        Assert.Same(error, typed.Error);
        Assert.Same(error, builtSet.Error);
    }

    [Fact]
    public async Task A_response_for_another_definition_throws()
    {
        var fake = new ClientTestKit.CapturingClient(QuestionSets.TriageDefinition());

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fake.EvaluateAsync<UrgencyCheck>("s"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fake.EvaluateAsync(QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out _).Build().Value, "s"));
    }

    [Fact]
    public void Invalid_state_throws_synchronously()
    {
        var fake = new ClientTestKit.CapturingClient();

        ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync<UrgencyCheck>((string)null!).AsTask());
        Assert.Equal(
            "state",
            ClientTestKit.ThrowsSynchronously<ArgumentException>(() => fake.EvaluateAsync<UrgencyCheck>(default(JsonElement)).AsTask()).ParamName);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void A_null_client_throws_synchronously()
    {
        IDecisionClient client = null!;
        using var document = JsonDocument.Parse("{}");
        var set = QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out _).Build().Value;

        Assert.Equal("client", Assert.Throws<ArgumentNullException>(() => client.GetService<DecisionClientMetadata>()).ParamName);
        Assert.Equal("client", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync<UrgencyCheck>("s").AsTask()).ParamName);
        Assert.Equal("client", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync<UrgencyCheck>(document.RootElement).AsTask()).ParamName);
        Assert.Equal("client", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateUtf8Async<UrgencyCheck>("{}"u8.ToArray()).AsTask()).ParamName);
        Assert.Equal(
            "client",
            ClientTestKit.ThrowsSynchronously<ArgumentNullException>(
                () => client.EvaluateAsync<TicketUrgency, TicketContext>(new TicketContext("s", "b"), TicketContextJsonContext.Default.TicketContext).AsTask()).ParamName);
        Assert.Equal("client", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync(set, "s").AsTask()).ParamName);
    }

    [Fact]
    public void GetService_of_T_returns_a_matching_service_or_null()
    {
        var fake = new ClientTestKit.CapturingClient();

        Assert.Same(fake, fake.GetService<ClientTestKit.CapturingClient>());
        Assert.Same(fake, fake.GetService<IDecisionClient>());
        Assert.Null(fake.GetService<DecisionClientMetadata>());
        Assert.Null(fake.GetService<ClientTestKit.CapturingClient>(serviceKey: "key"));
    }

    // The UTF-8 overload copies the caller's bytes once, so the caller may reuse its buffer as soon as the call returns.
    // That copy, length + 24 B, is its whole extra cost over the text overload, which allocates nothing for its state:
    // no JsonDocument is built from the bytes.
    [Fact]
    public void Utf8_state_costs_one_copy_per_call()
    {
        var client = new FixedClient(ClientTestKit.CapturingClient.Canned(UrgencyCheck.Definition));
        ReadOnlyMemory<byte> utf8 = Encoding.UTF8.GetBytes("""{"messages":[{"role":"user","content":"Help!"}]}""");

        var text = AllocationGate.MeasureBytesPerCallValueTask(1000, () => client.EvaluateAsync<UrgencyCheck>("text"));
        var state = AllocationGate.MeasureBytesPerCallValueTask(1000, () => client.EvaluateUtf8Async<UrgencyCheck>(utf8));
        var copy = AllocationGate.MeasureBytesPerCall(1000, () => _ = utf8.ToArray());

        Assert.True(copy >= utf8.Length, $"copy {copy} B");
        Assert.Equal(text + copy, state);
    }

    // The JsonElement overload clones the element once per call, so the caller may dispose its document as soon as the
    // call returns. That copy is its whole extra cost over the text overload; it measured 304 B per call for this
    // one-message log, a figure that follows the runtime's JsonDocument layout, so only the relation is asserted.
    [Fact]
    public void JsonElement_state_costs_one_clone_per_call()
    {
        var client = new FixedClient(ClientTestKit.CapturingClient.Canned(UrgencyCheck.Definition));
        using var document = JsonDocument.Parse("""{"messages":[{"role":"user","content":"Help!"}]}""");
        var element = document.RootElement;

        var text = AllocationGate.MeasureBytesPerCallValueTask(1000, () => client.EvaluateAsync<UrgencyCheck>("text"));
        var json = AllocationGate.MeasureBytesPerCallValueTask(1000, () => client.EvaluateAsync<UrgencyCheck>(element));
        var clone = AllocationGate.MeasureBytesPerCall(1000, () => _ = element.Clone());

        Assert.True(clone > 0, $"clone {clone} B");
        Assert.Equal(text + clone, json);
    }

    private sealed class FixedClient(DecisionResponse response) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Success(response));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
