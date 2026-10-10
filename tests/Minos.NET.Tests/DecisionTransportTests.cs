using System.Net;
using System.Text;
using Minos.Protocols;
using Minos.Serialization;
using Minos.Transport;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace Minos.Tests;

public sealed class DecisionTransportTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    [Fact]
    public async Task SendAsync_keeps_the_slash_in_a_runtime_path()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/base/") };
        _httpClients.Add(http);
        var pool = new CountingPool();
        var api = new DecisionApiClient(
            http,
            new SystemTextJsonSerializer(DecisionJsonContext.Default),
            new DecisionRawSerializer(pool),
            new DecisionErrorMapper(TimeProvider.System, disposed: null, SystemOneProtocol.Instance));
        using var body = RawJson.Create(pool, 16);
        "{}"u8.CopyTo(body.GetSpan(2));
        body.Advance(2);

        var result = await api.SendAsync(body, SystemOneProtocol.Instance.EndpointPath, "Bearer k", retryCount: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        result.Value.Dispose();
        Assert.Equal("/base/v1/systemone", ClientTestKit.OnlyRequest(handler).Uri!.AbsolutePath);
    }

    [Fact]
    public async Task Posts_the_protocol_request_to_its_endpoint()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, handler, pool);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.Answers[0].Value);
        Assert.Equal("jev-1.13.0", result.Value.Model);
        var sent = ClientTestKit.OnlyRequest(handler);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/v1/systemone", sent.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("Bearer test-key", sent.Authorization);
        Assert.Equal(
            """{"state":"a ticket","model":"jev-test-model","questions":""" + Encoding.UTF8.GetString(SystemOneProtocol.QuestionsJson(QuestionSets.UrgencyDefinition())) + "}",
            sent.Body);
        Assert.Null(sent.RetryCount);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task Sends_the_retry_count_on_a_retry_attempt()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler);

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s") { RetryAttempt = 2 });

        Assert.Equal("2", ClientTestKit.OnlyRequest(handler).RetryCount);
    }

    [Fact]
    public async Task A_request_model_overrides_the_configured_one()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler);

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s") { Model = "minos-other" });

        Assert.Contains("\"model\":\"minos-other\"", ClientTestKit.OnlyRequest(handler).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_an_error_status_through_the_protocol()
    {
        var handler = StubHandler.Json(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""");
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, handler, pool);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.Equal(DecisionErrorKind.RateLimited, result.Error.Kind);
        Assert.Equal(429, result.Error.StatusCode);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task An_unreadable_response_returns_every_buffer()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"answers":{}}""");
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, handler, pool);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task Does_not_retry_on_its_own()
    {
        var handler = StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
        var http = new HttpClient(handler);
        _httpClients.Add(http);
        var settings = DecisionClientSettings.Resolve(
            new DecisionClientOptions { ApiKey = "test-key", Model = ClientTestKit.TestModel, MaxRetries = 3, InitialBackoff = TimeSpan.FromMilliseconds(1), UseStandardPipeline = false },
            _ => null);
        using var client = new DecisionClient(settings, http, ownedHandler: null, TimeProvider.System);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.Equal(DecisionErrorKind.Overloaded, result.Error.Kind);
        ClientTestKit.OnlyRequest(handler);
    }

    [Fact]
    public void Metadata_names_the_provider_endpoint_and_model()
    {
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"), provider: DecisionProvider.OpenRouter);

        var metadata = client.GetService<DecisionClientMetadata>();

        Assert.Equal("openrouter", metadata!.ProviderName);
        Assert.Equal(ClientTestKit.TestModel, metadata.DefaultModel);
        Assert.NotNull(metadata.Endpoint);
        Assert.Same(client, client.GetService<DecisionClient>());
        Assert.Same(client, client.GetService<IDecisionClient>());
        Assert.Null(client.GetService<string>());
        Assert.Null(client.GetService<DecisionClientMetadata>(serviceKey: "key"));
    }

    [Fact]
    public void A_disposed_client_throws_at_entry()
    {
        var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"));
        client.Dispose();

        ClientTestKit.ThrowsSynchronously<ObjectDisposedException>(
            () => client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s")).AsTask());
    }

    [Fact]
    public async Task An_attempt_after_the_client_was_disposed_is_Disposed_without_being_sent()
    {
        var api = new CountingApi();
        var pool = new CountingPool();
        using var transport = new DecisionTransport(
            api, SystemOneProtocol.Instance, "Bearer k", new DecisionClientMetadata("typesafe", null, null), pool, static () => true);

        var call = transport.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.True(call.IsCompletedSuccessfully);
        var result = await call;
        Assert.Equal(DecisionErrorKind.Disposed, result.Error.Kind);
        Assert.Equal(0, api.Calls);
        Assert.Equal(0, pool.Rented);
        Assert.Equal(0, pool.Outstanding);
    }

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    // Counts every call; none of them is expected to run.
    private sealed class CountingApi : IDecisionApi
    {
        public int Calls { get; private set; }

        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
            => throw Called();

        public ValueTask<Result<RawJson, DecisionError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
            => throw Called();

        public ValueTask<Result<RawJson, DecisionError>> SendAsync(RawJson body, string path, string authorization, int? retryCount, CancellationToken ct)
            => throw Called();

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
            => throw Called();

        private InvalidOperationException Called()
        {
            Calls++;
            return new InvalidOperationException("The transport sent an attempt after disposal.");
        }
    }
}
