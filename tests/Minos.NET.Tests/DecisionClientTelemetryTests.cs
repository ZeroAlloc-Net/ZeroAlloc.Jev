using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Minos.Serialization;

namespace Minos.Tests;

/// <summary>What <see cref="DecisionClient"/> traces and measures for each operation, through the generated proxy.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class DecisionClientTelemetryTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public async Task RawEvaluation_IsOneEvaluateSpan_WithItsRequestTags()
    {
        using var capture = new TelemetryCapture();
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")));

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        var span = capture.Span();
        Assert.Equal("evaluate jev-latest", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        var start = capture.StartTags();
        Assert.Equal("evaluate", start.Tag("minos.operation"));
        Assert.Equal("typesafe", start.Tag("gen_ai.provider.name"));
        Assert.Equal("api.typesafe.ai", start.Tag("server.address"));
        Assert.Equal(443, start.Tag("server.port"));
        Assert.Equal(1, start.Tag("minos.request.question_count"));
        Assert.Equal("jev-1.13.0", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal(296, span.GetTagItem("gen_ai.usage.input_tokens"));
    }

    [Fact]
    public async Task TypedEvaluation_IsAnEvaluateSetSpan_WithTheDefinitionsQuestionCount()
    {
        using var capture = new TelemetryCapture();
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-choice.json")), pool);

        var result = await client.EvaluateAsync<DepartmentRouting>("Help!");

        Assert.Equal(Department.Billing, result.Value.Department.Value);
        Assert.Equal(0, pool.Outstanding);
        var span = capture.Span();
        Assert.Equal($"evaluate {ClientTestKit.TestModel}", span.DisplayName);
        Assert.Equal("evaluate-set", capture.StartTags().Tag("minos.operation"));
        Assert.Equal(ClientTestKit.TestModel, capture.StartTags().Tag("gen_ai.request.model"));
        Assert.Equal(1, capture.StartTags().Tag("minos.request.question_count"));
        Assert.Equal("jev-1.13.0", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal([0.81], capture.Points("minos.answer.confidence").Select(point => point.Value));
    }

    [Fact]
    public async Task TypedEvaluation_CompletingAsynchronously_ReturnsEveryBuffer()
    {
        using var capture = new TelemetryCapture();
        var pool = new CountingPool();
        var handler = new StubHandler(async (_, _) =>
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture.Text("response-choice.json"), Encoding.UTF8, "application/json") };
        });
        using var client = ClientTestKit.Client(_httpClients, handler, pool);

        var result = await client.EvaluateAsync<DepartmentRouting>("Help!");

        Assert.Equal(Department.Billing, result.Value.Department.Value);
        Assert.True(pool.Rented > 0);
        Assert.Equal(0, pool.Outstanding);
        Assert.Equal("evaluate-set", capture.StartTags().Tag("minos.operation"));
        Assert.Equal("jev-1.13.0", capture.Span().GetTagItem("gen_ai.response.model"));
    }

    [Fact]
    public async Task TypedEvaluation_CancelledWhilePending_Throws_AndReturnsEveryBuffer()
    {
        using var capture = new TelemetryCapture();
        var pool = new CountingPool();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = ClientTestKit.Client(_httpClients, handler, pool);
        using var cancellation = new CancellationTokenSource();

        var pending = client.EvaluateAsync<DepartmentRouting>("Help!", cancellation.Token).AsTask();
        await started.Task;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);

        Assert.True(pool.Rented > 0);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task CancelledCall_MarksTheSpanError_WithTheExceptionsTypeAndNoDescription()
    {
        using var capture = new TelemetryCapture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = ClientTestKit.Client(_httpClients, handler);
        using var cancellation = new CancellationTokenSource();

        var pending = client.EvaluateAsync(Request(), cancellation.Token).AsTask();
        await started.Task;
        await cancellation.CancelAsync();
        var raised = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);

        var span = capture.Span();
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.True(string.IsNullOrEmpty(span.StatusDescription));
        Assert.Equal(raised.GetType().FullName, span.GetTagItem("error.type"));
        var point = capture.OnlyPoint();
        Assert.Equal(1, point.Tags.Count(tag => string.Equals(tag.Key, "error.type", StringComparison.Ordinal)));
        Assert.Equal(raised.GetType().FullName, point.Tag("error.type"));
    }

    [Fact]
    public async Task EveryTypedOverload_IsTraced()
    {
        using var capture = new TelemetryCapture();
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")));

        using var state = JsonDocument.Parse("{\"m\":1}");

        await client.EvaluateAsync<UrgencyCheck>("Help!");
        await client.EvaluateAsync<UrgencyCheck>(state.RootElement);
        await client.EvaluateUtf8Async<UrgencyCheck>("{\"m\":1}"u8.ToArray());
        await client.EvaluateAsync<TicketUrgency, TicketContext>(
            new TicketContext("Payouts failing", "Help!"), TicketContextJsonContext.Default.TicketContext);

        var spans = capture.Spans("Minos");
        Assert.Equal(4, spans.Length);
        Assert.All(spans, span => Assert.Equal("evaluate-set", span.GetTagItem("minos.operation")));
    }

    [Fact]
    public async Task TypedParseFailure_IsAnInvalidResponseError()
    {
        using var capture = new TelemetryCapture();
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(
            _httpClients, StubHandler.Json(HttpStatusCode.OK, """{"model":"jev-1.13.0","answers":null}"""), pool);

        var result = await client.EvaluateAsync<UrgencyCheck>("Help!");

        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(0, pool.Outstanding);
        var span = capture.Span();
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("InvalidResponse", span.GetTagItem("error.type"));
        Assert.Equal("InvalidResponse", capture.OnlyPoint().Tag("error.type"));
    }

    [Fact]
    public async Task BuiltSetEvaluation_IsAnEvaluateSetSpan()
    {
        using var capture = new TelemetryCapture();
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")), pool);
        var set = BuiltSets.UrgencyOnly();

        var result = await client.EvaluateAsync(set, "Help!");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, pool.Outstanding);
        Assert.Equal("evaluate-set", capture.StartTags().Tag("minos.operation"));
        Assert.Equal(1, capture.StartTags().Tag("minos.request.question_count"));
        Assert.Empty(capture.Points("minos.answer.confidence"));
    }

    [Fact]
    public async Task BuiltSetFailure_IsAnErrorWithItsKind_AndReturnsEveryBuffer()
    {
        using var capture = new TelemetryCapture();
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"), pool);
        var set = BuiltSets.UrgencyOnly();

        var result = await client.EvaluateAsync(set, "Help!");

        Assert.Equal(DecisionErrorKind.Overloaded, result.Error.Kind);
        Assert.Equal(0, pool.Outstanding);
        Assert.Equal(ActivityStatusCode.Error, capture.Span().Status);
        Assert.Equal("Overloaded", capture.Span().GetTagItem("error.type"));
        Assert.Equal("Overloaded", capture.OnlyPoint().Tag("error.type"));
    }

    [Fact]
    public async Task HttpFailure_IsAnErrorWithItsKind()
    {
        using var capture = new TelemetryCapture();
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"detail":"bad"}"""));

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        Assert.Equal(ActivityStatusCode.Error, capture.Span().Status);
        Assert.Equal("Validation", capture.Span().GetTagItem("error.type"));
    }

    [Fact]
    public async Task ListModels_IsAListModelsSpan()
    {
        using var capture = new TelemetryCapture();
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json")));

        var result = await client.ListModelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("list_models", capture.Span().DisplayName);
        Assert.Equal("list-models", capture.StartTags().Tag("minos.operation"));
        Assert.Equal("gen_ai.client.operation.duration", capture.OnlyPoint().Metric);
    }

    [Fact]
    public async Task OpenRouterListModels_RecordsNothing()
    {
        using var capture = new TelemetryCapture();
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, provider: DecisionProvider.OpenRouter);

        var result = await client.ListModelsAsync();

        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Empty(handler.Requests);
        Assert.Empty(capture.Spans("Minos"));
        Assert.Empty(capture.AllPoints);
    }

    [Fact]
    public async Task OpenRouterEvaluation_CarriesItsProviderAddressIdAndCost()
    {
        using var capture = new TelemetryCapture();
        using var client = ClientTestKit.Client(
            _httpClients, StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-openrouter.json")), provider: DecisionProvider.OpenRouter);

        await client.EvaluateAsync(Request());

        var start = capture.StartTags();
        Assert.Equal("openrouter", start.Tag("gen_ai.provider.name"));
        Assert.Equal("openrouter.ai", start.Tag("server.address"));
        Assert.Equal("gen-1727400000-abc123", capture.Span().GetTagItem("gen_ai.response.id"));
        Assert.Equal(0.000296, capture.Span().GetTagItem("minos.usage.cost"));
    }

    [Fact]
    public async Task WithLogging_TheLogAndTheSpanAreBothRecorded()
    {
        using var capture = new TelemetryCapture();
        using var logs = new LogCapture();
        var http = new HttpClient(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")));
        _httpClients.Add(http);
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "test-key", MaxRetries = 0 }, logs.Factory);

        await client.EvaluateAsync(Request());

        Assert.Equal([1001], logs.EventIds);
        Assert.Equal("evaluate", capture.Span().GetTagItem("minos.operation"));
    }

    private static SystemOneRequest Request()
        => JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;
}
