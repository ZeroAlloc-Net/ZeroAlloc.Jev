using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.Serialization;
using Minos.Transport;

namespace Minos.Tests;

public sealed class DecisionClientTests : IDisposable
{
    private const string NullAnswerResponse = """{"model":"m","answers":{"x":null},"usage":{"input_tokens":1,"output_tokens":1}}""";

    private readonly List<HttpClient> _borrowedHttpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _borrowedHttpClients.Count; i++)
        {
            _borrowedHttpClients[i].Dispose();
        }
    }

    [Fact]
    public async Task Evaluate_TypeSafe_PostsTheRequestWithBearerAuth()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = Borrowing(handler);

        var result = await client.EvaluateAsync(NoulRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, Assert.IsType<NoulAnswer>(result.Value.Answers["is_urgent"]).Noul);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var sent = Assert.Single(handler.Requests);
#pragma warning restore HLQ005
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri("https://api.typesafe.ai/v1/systemone"), sent.Uri);
        Assert.Equal("Bearer test-key", sent.Authorization);
        Assert.True(JsonNode.DeepEquals(Fixture.Load("request-noul.json"), JsonNode.Parse(sent.Body!)));
    }

    [Fact]
    public async Task Evaluate_OpenRouter_UsesTheApiPrefix()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-openrouter.json"));
        using var client = Borrowing(handler, DecisionProvider.OpenRouter);

        var result = await client.EvaluateAsync(NoulRequest());

        Assert.Equal("gen-1727400000-abc123", result.Value.Id);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/systemone"), Assert.Single(handler.Requests).Uri);
#pragma warning restore HLQ005
    }

    [Fact]
    public async Task ListModels_TypeSafe_GetsTheModels()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json"));
        using var client = Borrowing(handler);

        var result = await client.ListModelsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Models);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var sent = Assert.Single(handler.Requests);
#pragma warning restore HLQ005
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(new Uri("https://api.typesafe.ai/v1/models"), sent.Uri);
        Assert.Equal("Bearer test-key", sent.Authorization);
    }

    [Fact]
    public async Task ListModels_OpenRouter_IsUnsupported_WithoutARequest()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json"));
        using var client = Borrowing(handler, DecisionProvider.OpenRouter);

        var result = await client.ListModelsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(401, DecisionErrorKind.Unauthorized)]
    [InlineData(422, DecisionErrorKind.Validation)]
    [InlineData(429, DecisionErrorKind.RateLimited)]
    [InlineData(529, DecisionErrorKind.Overloaded)]
    [InlineData(500, DecisionErrorKind.Server)]
    public async Task Evaluate_ErrorStatus_MapsToKind(int status, DecisionErrorKind expected)
    {
        using var client = Borrowing(StubHandler.Json((HttpStatusCode)status, "{}"));

        var result = await client.EvaluateAsync(NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(expected, result.Error.Kind);
        Assert.Equal(status, result.Error.StatusCode);
    }

    [Fact]
    public async Task Evaluate_422_CarriesTheJsonDetail()
    {
        using var client = Borrowing(StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"detail":"questions.is_urgent.instructions is required"}"""));

        var result = await client.EvaluateAsync(NoulRequest());

        Assert.Equal("questions.is_urgent.instructions is required", result.Error.Detail!.Value.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Evaluate_429_CarriesRetryAfter()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", "3");
            return Task.FromResult(response);
        });
        using var client = Borrowing(handler);

        var result = await client.EvaluateAsync(NoulRequest());

        Assert.Equal(TimeSpan.FromSeconds(3), result.Error.RetryAfter);
    }

    [Fact]
    public async Task Evaluate_TransportFailure_IsNetwork()
    {
        using var client = Borrowing(new StubHandler((_, _) => throw new HttpRequestException("connection refused")));

        var result = await client.EvaluateAsync(NoulRequest());

        Assert.Equal(DecisionErrorKind.Network, result.Error.Kind);
        Assert.IsType<HttpRequestException>(result.Error.Exception);
    }

    [Fact]
    public async Task Evaluate_ClientTimeout_IsTimeout()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = Owning(handler, timeout: TimeSpan.FromMilliseconds(50));

        var result = await client.EvaluateAsync(NoulRequest());

        Assert.Equal(DecisionErrorKind.Timeout, result.Error.Kind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData(NullAnswerResponse)]
    public async Task Evaluate_UnreadableSuccessBody_IsInvalidResponse(string body)
    {
        using var client = Borrowing(StubHandler.Json(HttpStatusCode.OK, body));

        var result = await client.EvaluateAsync(NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task ListModels_UnreadableSuccessBody_IsInvalidResponse(string body)
    {
        using var client = Borrowing(StubHandler.Json(HttpStatusCode.OK, body));

        var result = await client.ListModelsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
    }

    [Fact]
    public async Task Evaluate_CallerCancellation_Throws()
    {
        using var client = Borrowing(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.EvaluateAsync(NoulRequest(), cancellation.Token));
    }

    [Fact]
    public async Task OwnedClient_SendsUserAgent_AndIsDisposedWithTheClient()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        var client = Owning(handler);

        _ = await client.EvaluateAsync(NoulRequest());
        client.Dispose();

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var sent = Assert.Single(handler.Requests);
#pragma warning restore HLQ005
        Assert.StartsWith("Minos.NET/", sent.UserAgent, StringComparison.Ordinal);
        Assert.DoesNotContain('+', sent.UserAgent);
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task BorrowedClient_KeepsItsBaseAddress_AndSurvivesDispose()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://proxy.local/minos/") };
        var client = new DecisionClient(Settings(), http, ownedHandler: null, TimeProvider.System);

        _ = await client.EvaluateAsync(NoulRequest());
        client.Dispose();

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        Assert.Equal(new Uri("http://proxy.local/minos/v1/systemone"), Assert.Single(handler.Requests).Uri);
#pragma warning restore HLQ005
        Assert.False(handler.Disposed);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void BorrowedClient_WithBaseAddressMissingTrailingSlash_Throws()
    {
        using var handler = StubHandler.Json(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api") };

        var exception = Assert.Throws<ArgumentException>(
            () => new DecisionClient(Settings(), http, ownedHandler: null, TimeProvider.System));

        Assert.Equal("httpClient", exception.ParamName);
    }

    [Fact]
    public void BorrowedClient_WithBaseAddressQuery_Throws()
    {
        using var handler = StubHandler.Json(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/?key=value") };

        var exception = Assert.Throws<ArgumentException>(
            () => new DecisionClient(Settings(), http, ownedHandler: null, TimeProvider.System));

        Assert.Equal("httpClient", exception.ParamName);
    }

    [Fact]
    public void BorrowedClient_WithBaseAddressFragment_Throws()
    {
        using var handler = StubHandler.Json(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/#frag") };

        var exception = Assert.Throws<ArgumentException>(
            () => new DecisionClient(Settings(), http, ownedHandler: null, TimeProvider.System));

        Assert.Equal("httpClient", exception.ParamName);
    }

    [Fact]
    public async Task BorrowedClientWithoutBaseAddress_GetsTheProviderAddress()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var http = new HttpClient(handler);
        var client = new DecisionClient(Settings(), http, ownedHandler: null, TimeProvider.System);

        _ = await client.EvaluateAsync(NoulRequest());
        client.Dispose();

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        Assert.Equal(new Uri("https://api.typesafe.ai/v1/systemone"), Assert.Single(handler.Requests).Uri);
#pragma warning restore HLQ005
    }

    [Fact]
    public async Task DisposedClient_Throws()
    {
        var client = Borrowing(StubHandler.Json(HttpStatusCode.OK, "{}"));
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(NoulRequest()));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.ListModelsAsync());
    }

    [Fact]
    public async Task NullRequest_Throws()
    {
        using var client = Borrowing(StubHandler.Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await client.EvaluateAsync((SystemOneRequest)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await client.EvaluateAsync((DecisionRequest)null!));
    }

    [Fact]
    public void PublicConstructors_ValidateArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new DecisionClient((HttpClient)null!));
        Assert.Throws<ArgumentNullException>(() => new DecisionClient(null!, new DecisionClientOptions { ApiKey = "k" }));

        // A null httpClient must be reported even when the options would otherwise be invalid.
        Assert.Throws<ArgumentNullException>(() => new DecisionClient(null!, new DecisionClientOptions { Provider = (DecisionProvider)42 }));

        using var client = new DecisionClient(new DecisionClientOptions { ApiKey = "k", BaseAddress = new Uri("http://localhost/") });
        Assert.NotNull(client);
    }

    // maxRetries defaults to 0 so each test here sees exactly one outcome per status; DecisionClientRetryTests covers retries.
    private static DecisionClientSettings Settings(DecisionProvider provider = DecisionProvider.TypeSafe, TimeSpan? timeout = null, int maxRetries = 0)
        => DecisionClientSettings.Resolve(
            new DecisionClientOptions
            {
                ApiKey = "test-key",
                Provider = provider,
                Timeout = timeout ?? TimeSpan.FromSeconds(60),
                MaxRetries = maxRetries,
            },
            _ => null);

    private DecisionClient Borrowing(StubHandler handler, DecisionProvider provider = DecisionProvider.TypeSafe)
    {
        var http = new HttpClient(handler);
        _borrowedHttpClients.Add(http);
        return new(Settings(provider), http, ownedHandler: null, TimeProvider.System);
    }

    private static DecisionClient Owning(StubHandler handler, TimeSpan? timeout = null)
        => new(Settings(timeout: timeout), httpClient: null, handler, TimeProvider.System);

    [Theory]
    [InlineData(typeof(IDecisionClient), 1)]
    [InlineData(typeof(DecisionClient), 3)]
    [InlineData(typeof(DecisionClientExtensions), 5)]
    public void EveryCancellationTokenParameter_IsNamedCancellationToken(Type type, int count)
    {
        // A parameter name is public API: callers can pass the token by name, as the BCL names it.
        var names = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(CancellationToken))
            .Select(parameter => parameter.Name)
            .ToArray();

        Assert.Equal(count, names.Length);
        Assert.All(names, name => Assert.Equal("cancellationToken", name));
    }

    private static SystemOneRequest NoulRequest()
        => JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;
}
