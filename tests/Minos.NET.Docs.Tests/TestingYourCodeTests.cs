using System.Text.Json;

namespace Minos.Docs.Tests;

/// <summary>The claims the testing page makes about fakes and canned responses.</summary>
public sealed class TestingYourCodeTests
{
    [Fact]
    public async Task EveryTypedOverload_GoesThroughTheFakesOneEvaluateMethod()
    {
        IDecisionClient client = FakeDecision.Answering(0.9, TriageDesk.Billing, 0.8);

        Assert.True((await client.EvaluateAsync<TriageQuestions>("text")).IsSuccess);
        Assert.True((await client.EvaluateAsync<TriageQuestions>("text", CancellationToken.None)).IsSuccess);
        Assert.True((await client.EvaluateUtf8Async<TriageQuestions>("\"text\""u8.ToArray())).IsSuccess);
        Assert.True((await client.EvaluateAsync<TriageQuestions>(JsonElement.Parse("\"text\""))).IsSuccess);
        Assert.True((await client.EvaluateAsync<TriageQuestions>(JsonElement.Parse("{}"), CancellationToken.None)).IsSuccess);

        Assert.Equal(5, ((FakeDecision)client).Requests.Count);
    }

    [Fact]
    public async Task AChoiceAnswer_IsReadByOptionPosition_FromTheFake()
    {
        IDecisionClient client = FakeDecision.Answering(0.1, TriageDesk.ProductTeam, 0.9);

        var result = await client.EvaluateAsync<TriageQuestions>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(TriageDesk.ProductTeam, result.Value.Desk.Value);
        Assert.Equal(0.9, result.Value.Desk.Confidence);
    }

    [Fact]
    public async Task AResponseBuiltForTheAskedDefinition_IsReadThroughABuiltSet()
    {
        var set = QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out var urgent).Build().Value;
        IDecisionClient client = new FakeDecision(ZeroAlloc.Results.Result<DecisionResponse, DecisionError>.Success(
            new DecisionResponse(set.Definition, [QuestionAnswer.Noul(0.9)])));

        var result = await client.EvaluateAsync(set, "text");

        Assert.True(result.Value.Get(urgent).Value);
    }

    [Fact]
    public async Task AResponseForAnotherDefinition_Throws()
    {
        var set = QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out _).Build().Value;
        var other = QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out _).Build().Value;
        IDecisionClient client = new FakeDecision(ZeroAlloc.Results.Result<DecisionResponse, DecisionError>.Success(
            new DecisionResponse(other.Definition, [QuestionAnswer.Noul(0.9)])));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.EvaluateAsync(set, "text"));
    }

    [Fact]
    public async Task ATypedCall_ReadsOnlyTheAnswersOfACannedBody()
    {
        const string body = """
            { "answers": { "is_urgent": { "type": "noul", "noul": 0.9 },
                           "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }
            """;

        var answers = await CannedDecision.EvaluateAsync<TriageQuestions>(body, "text");

        Assert.True(answers.IsUrgent.Value);
        Assert.Equal(TriageDesk.Billing, answers.Desk.Value);
    }

    [Theory]
    [InlineData("""{ "answers": { "is_urgent": { "noul": 0.9 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }""")]
    [InlineData("""{ "answers": { "is_urgent": { "type": "noul", "noul": 0.9 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 } } } }""")]
    [InlineData("""{ "answers": { "is_urgent": { "type": "noul", "noul": 0.9 } } }""")]
    public async Task ACannedBodyThatLeavesOutAKeyField_IsAnInvalidResponse(string body)
    {
        var (http, client, _) = CannedDecision.Client(body);
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync<TriageQuestions>("text", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        }
    }

    [Fact]
    public async Task ARealClientWithoutABaseAddress_NeedsNoNetwork()
    {
        var handler = new CannedHandler(System.Net.HttpStatusCode.OK, """{ "answers": { "is_urgent": { "type": "noul", "noul": 0.1 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }""");
        using var http = new HttpClient(handler);

        Assert.Null(http.BaseAddress);
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "test-key", MaxRetries = 0 });

        Assert.NotNull(http.BaseAddress);
        Assert.Equal(TriageRoute.Queue, await new TicketTriager(client).RouteAsync("text", CancellationToken.None));
    }
}
