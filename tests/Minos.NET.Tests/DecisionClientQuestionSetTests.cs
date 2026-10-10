using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.Serialization;
using static Minos.Tests.ClientTestKit;

namespace Minos.Tests;

/// <summary>A built set evaluates the same through <see cref="DecisionClientExtensions"/> and through DecisionClient's pooled path.</summary>
public sealed class DecisionClientQuestionSetTests : IDisposable
{
    private const string ResponseJson = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95},"department":{"type":"choice","choice":"technical","probabilities":{"billing":0.1,"technical":0.8,"sales":0.1},"confidence":0.8},"product":{"type":"choice","choice":"pro-plan","probabilities":{"pro-plan":0.6,"team-plan":0.4},"confidence":0.6},"effort":{"type":"score","score":1.2,"legend":{"0":"Minutes","1":"Hours","2":"Days"},"probabilities":{"0":0.1,"1":0.6,"2":0.3},"confidence":0.7}},"usage":{"input_tokens":10,"output_tokens":5}}""";

    private static readonly string ResponseWithoutLegend = ResponseJson.Replace(
        "\"legend\":{\"0\":\"Minutes\",\"1\":\"Hours\",\"2\":\"Days\"},", string.Empty, StringComparison.Ordinal);

    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public Task TextState_BothPathsAgree() => AssertBothPathsAgree("Help! My payouts have been failing for 3 days.");

    [Fact]
    public Task JsonState_BothPathsAgree()
        => AssertBothPathsAgree(DecisionContent.FromUtf8Json("""{"messages":[{"role":"user","content":"Help!"}]}"""u8));

    [Fact]
    public void InvalidArguments_ThrowSynchronously_WithoutARequest()
    {
        var set = Set(out _, out _, out _, out _);
        var handler = StubHandler.Json(HttpStatusCode.OK, ResponseJson);
        var pool = new CountingPool();
        using var client = Client(handler, pool);
        IDecisionClient fake = new CapturingClient(responseJson: ResponseJson);

        Assert.Equal("questionSet", ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync((QuestionSet)null!, "x").AsTask()).ParamName);
        Assert.Equal("state", ThrowsSynchronously<ArgumentException>(() => client.EvaluateAsync(set, default(DecisionContent)).AsTask()).ParamName);
        Assert.Equal("questionSet", ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync((QuestionSet)null!, "x").AsTask()).ParamName);
        Assert.Equal("state", ThrowsSynchronously<ArgumentException>(() => fake.EvaluateAsync(set, default(DecisionContent)).AsTask()).ParamName);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task DisposedClient_Throws_AfterCheckingArguments()
    {
        var set = Set(out _, out _, out _, out _);
        var client = Client(StubHandler.Json(HttpStatusCode.OK, ResponseJson));
        client.Dispose();

        ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync((QuestionSet)null!, "x").AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(set, "x"));
    }

    [Fact]
    public async Task MissingAnswer_IsInvalidResponse_AndReturnsTheBuffers()
    {
        var set = Set(out _, out _, out _, out _);
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")), pool);

        var result = await client.EvaluateAsync(set, "x");

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.IsType<JsonException>(result.Error.Exception);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task ScoreAnswerWithoutLegend_IsInvalidResponse_AndReturnsTheBuffers()
    {
        var set = Set(out _, out _, out _, out _);
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, ResponseWithoutLegend), pool);

        var result = await client.EvaluateAsync(set, "x");

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.IsType<JsonException>(result.Error.Exception);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task ExtensionPath_MissingAnswer_IsInvalidResponse()
    {
        var set = Set(out _, out _, out _, out _);
        IDecisionClient fake = new CapturingClient(responseJson: Fixture.Text("response-noul.json"));

        var result = await fake.EvaluateAsync(set, "x");

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, DecisionErrorKind.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, DecisionErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, DecisionErrorKind.Server)]
    public async Task ErrorStatus_MapsToTheErrorKind_AndReturnsTheBuffers(HttpStatusCode status, DecisionErrorKind kind)
    {
        var set = Set(out _, out _, out _, out _);
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(status, """{"detail":"nope"}"""), pool);

        var result = await client.EvaluateAsync(set, "x");

        Assert.True(result.IsFailure);
        Assert.Equal(kind, result.Error.Kind);
        Assert.Equal((int)status, result.Error.StatusCode);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task CallerCancellation_Throws_AndReturnsTheBuffers()
    {
        var set = Set(out _, out _, out _, out _);
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, ResponseJson), pool);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.EvaluateAsync(set, "x", cancellation.Token));

        Assert.True(pool.Rented > 0);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task OptionsModel_IsSentForABuiltSet_AsReadWhenTheClientWasConstructed()
    {
        var set = Set(out _, out _, out _, out _);
        var handler = StubHandler.Json(HttpStatusCode.OK, ResponseJson);
        var http = new HttpClient(handler);
        _httpClients.Add(http);
        var options = new DecisionClientOptions { ApiKey = "test-key", Model = "jev-first", MaxRetries = 0 };
        using var client = new DecisionClient(http, options);

        // The client resolved its options when it was constructed, so a later change does not reach it.
        options.Model = "jev-second";
        var result = await client.EvaluateAsync(set, "x", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("jev-first", JsonNode.Parse(OnlyRequest(handler).Body!)!["model"]!.GetValue<string>());
    }

    private async Task AssertBothPathsAgree(DecisionContent state)
    {
        var set = Set(out var urgent, out var department, out var product, out var effort);
        var fake = new CapturingClient(responseJson: ResponseJson);

        var viaDefault = await ((IDecisionClient)fake).EvaluateAsync(set, state);
        var captured = fake.OnlyRequest();
        Assert.Same(set.Definition, captured.Definition);
        Assert.Null(captured.Model);
        var expected = ExpectedBody(captured);

        var handler = StubHandler.Json(HttpStatusCode.OK, ResponseJson);
        var pool = new CountingPool();
        using var client = Client(handler, pool);
        var viaClient = await client.EvaluateAsync(set, state, CancellationToken.None);

        var sent = JsonNode.Parse(OnlyRequest(handler).Body!);
        Assert.True(JsonNode.DeepEquals(expected, sent), sent?.ToJsonString());
        Assert.True(viaDefault.IsSuccess);
        Assert.True(viaClient.IsSuccess);
        Assert.Equal(viaDefault.Value.Get(urgent).Probability, viaClient.Value.Get(urgent).Probability);
        Assert.Equal(viaDefault.Value.Get(department), viaClient.Value.Get(department));
        Assert.Equal(viaDefault.Value.Get(product), viaClient.Value.Get(product));
        Assert.Equal(viaDefault.Value.Get(effort), viaClient.Value.Get(effort));
        Assert.True(pool.Rented >= 2, $"rented {pool.Rented}");
        Assert.Equal(0, pool.Outstanding);
    }

    private static QuestionSet Set(
        out NoulHandle urgent, out ChoiceHandle<Department> department, out KeyedChoiceHandle product, out KeyedScoreHandle effort)
    {
        var built = QuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out urgent)
            .Choice<Department>("department", "Which team should handle this?", out department, o => o
                .Describe(Department.Billing, "Payments, invoicing, refunds"))
            .Choice("product", "Which product?", out product, o => o.Option("pro-plan", "The Pro subscription").Option("team-plan"))
            .Score("effort", "How much effort?", out effort, l => l.Level("Minutes").Level("Hours").Level("Days"))
            .Build();
        Assert.True(built.IsSuccess);
        return built.Value;
    }

    private DecisionClient Client(StubHandler handler, CountingPool? pool = null) => ClientTestKit.Client(_httpClients, handler, pool);
}
