using System.Net;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZeroAlloc.Results;
using Minos.Shared;

namespace Minos.Benchmarks;

/// <summary>Benchmarks <see cref="DecisionClient"/>'s hot paths against an in-memory <see cref="HttpMessageHandler"/>,
/// through the public API only, since <c>DecisionJsonContext</c> is internal.</summary>
[MemoryDiagnoser]
public class ClientBenchmarks
{
    internal const string NoulResponseJson = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    internal const string ModelsResponseJson = """{"models":[{"name":"jev-latest","description":"The most recent stable, official release.","release_date":"2026-09-15"}]}""";
    internal const string TriageResponseJson = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    private HttpClient _evaluateHttp = null!;
    private HttpClient _listModelsHttp = null!;
    private HttpClient _typedEvaluateHttp = null!;
    private HttpClient _typedEvaluateNoulHttp = null!;
    private DecisionClient _evaluateClient = null!;
    private DecisionClient _listModelsClient = null!;
    private DecisionClient _typedEvaluateClient = null!;
    private DecisionClient _typedEvaluateNoulClient = null!;
    private HttpClient _evaluateLoggedHttp = null!;
    private HttpClient _typedEvaluateLoggedHttp = null!;
    private DecisionClient _evaluateLoggedClient = null!;
    private DecisionClient _typedEvaluateLoggedClient = null!;
    private HttpClient _evaluateYieldingHttp = null!;
    private HttpClient _evaluateYieldingLoggedHttp = null!;
    private HttpClient _evaluateYieldingNullLoggedHttp = null!;
    private DecisionClient _evaluateYieldingNullLoggedClient = null!;
    private DecisionClient _evaluateYieldingClient = null!;
    private DecisionClient _evaluateYieldingLoggedClient = null!;
    private HttpClient _typedEvaluateYieldingHttp = null!;
    private DecisionClient _typedEvaluateYieldingClient = null!;
    private SystemOneRequest _request = null!;
    private string _typedState = null!;

    [GlobalSetup]
    public void Setup()
    {
        (_evaluateHttp, _evaluateClient) = CreateClient(NoulResponseJson);
        (_listModelsHttp, _listModelsClient) = CreateClient(ModelsResponseJson);
        (_typedEvaluateHttp, _typedEvaluateClient) = CreateClient(TriageResponseJson);
        (_typedEvaluateNoulHttp, _typedEvaluateNoulClient) = CreateClient(NoulResponseJson);
        (_evaluateLoggedHttp, _evaluateLoggedClient) = CreateClient(NoulResponseJson, DiscardingLoggerFactory.Instance);
        (_typedEvaluateLoggedHttp, _typedEvaluateLoggedClient) = CreateClient(TriageResponseJson, DiscardingLoggerFactory.Instance);
        (_evaluateYieldingHttp, _evaluateYieldingClient) = CreateClient(new YieldingHandler(HttpStatusCode.OK, NoulResponseJson), loggerFactory: null);
        (_evaluateYieldingLoggedHttp, _evaluateYieldingLoggedClient) = CreateClient(new YieldingHandler(HttpStatusCode.OK, NoulResponseJson), DiscardingLoggerFactory.Instance);
        (_evaluateYieldingNullLoggedHttp, _evaluateYieldingNullLoggedClient) = CreateClient(new YieldingHandler(HttpStatusCode.OK, NoulResponseJson), NullLoggerFactory.Instance);
        (_typedEvaluateYieldingHttp, _typedEvaluateYieldingClient) = CreateClient(new YieldingHandler(HttpStatusCode.OK, TriageResponseJson), loggerFactory: null);
        _request = Request();
        _typedState = "Help! My payouts have been failing for 3 days.";
    }

    /// <summary>The one-question Noul request that <see cref="EvaluateAsync"/> and the dependency injection benchmarks send.</summary>
    internal static SystemOneRequest Request() => new()
    {
        State = "Help! My payouts have been failing for 3 days.",
        Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
        {
            ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
        },
    };

    [GlobalCleanup]
    public void Cleanup()
    {
        _evaluateClient.Dispose();
        _listModelsClient.Dispose();
        _typedEvaluateClient.Dispose();
        _typedEvaluateNoulClient.Dispose();
        _evaluateHttp.Dispose();
        _listModelsHttp.Dispose();
        _typedEvaluateHttp.Dispose();
        _typedEvaluateNoulHttp.Dispose();
        _evaluateLoggedClient.Dispose();
        _typedEvaluateLoggedClient.Dispose();
        _evaluateYieldingClient.Dispose();
        _evaluateYieldingLoggedClient.Dispose();
        _evaluateYieldingNullLoggedClient.Dispose();
        _evaluateLoggedHttp.Dispose();
        _typedEvaluateLoggedHttp.Dispose();
        _evaluateYieldingHttp.Dispose();
        _evaluateYieldingLoggedHttp.Dispose();
        _evaluateYieldingNullLoggedHttp.Dispose();
        _typedEvaluateYieldingClient.Dispose();
        _typedEvaluateYieldingHttp.Dispose();
    }

    /// <summary><see cref="DecisionClient.EvaluateAsync"/> over a fixed Noul response.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync() => _evaluateClient.EvaluateAsync(_request);

    /// <summary><see cref="DecisionClient.ListModelsAsync"/> over a fixed models response.</summary>
    [Benchmark]
    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync() => _listModelsClient.ListModelsAsync();

    /// <summary><see cref="DecisionClientExtensions.EvaluateAsync{T}(IDecisionClient, string)"/> over a fixed triage (three-answer) response,
    /// through the raw, pooled-buffer path.</summary>
    [Benchmark]
    public ValueTask<Result<BenchTriage, DecisionError>> TypedEvaluateAsync() => _typedEvaluateClient.EvaluateAsync<BenchTriage>(_typedState);

    /// <summary><see cref="DecisionClientExtensions.EvaluateAsync{T}(IDecisionClient, string)"/> over the same fixed Noul (one-answer) response as
    /// <see cref="EvaluateAsync"/>, through the raw, pooled-buffer path, so the typed and untyped calls compare
    /// like for like.</summary>
    [Benchmark]
    public ValueTask<Result<BenchUrgency, DecisionError>> TypedEvaluateNoulAsync() => _typedEvaluateNoulClient.EvaluateAsync<BenchUrgency>(_typedState);

    /// <summary><see cref="EvaluateAsync"/> through a logger enabled at every level that discards everything.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateWithDiscardingLoggerAsync() => _evaluateLoggedClient.EvaluateAsync(_request);

    /// <summary><see cref="TypedEvaluateAsync"/> through a logger enabled at every level that discards everything.</summary>
    [Benchmark]
    public ValueTask<Result<BenchTriage, DecisionError>> TypedEvaluateWithDiscardingLoggerAsync()
        => _typedEvaluateLoggedClient.EvaluateAsync<BenchTriage>(_typedState);

    /// <summary><see cref="EvaluateAsync"/> over a handler that completes asynchronously, with no logger.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateYieldingAsync() => _evaluateYieldingClient.EvaluateAsync(_request);

    /// <summary><see cref="EvaluateYieldingAsync"/> through <see cref="NullLoggerFactory"/>, whose logger has every
    /// level disabled, so it must cost nothing over the unlogged call.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateYieldingWithNullLoggerAsync() => _evaluateYieldingNullLoggedClient.EvaluateAsync(_request);

    /// <summary><see cref="EvaluateYieldingAsync"/> through a logger enabled at every level that discards everything,
    /// so the logging wrappers' async state machines run.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateYieldingWithDiscardingLoggerAsync() => _evaluateYieldingLoggedClient.EvaluateAsync(_request);

    /// <summary><see cref="TypedEvaluateAsync"/> over a handler that completes asynchronously, with nothing listening:
    /// what a real typed call pays, the telemetry unwrap's state machine included.</summary>
    [Benchmark]
    public ValueTask<Result<BenchTriage, DecisionError>> TypedEvaluateYieldingAsync() => _typedEvaluateYieldingClient.EvaluateAsync<BenchTriage>(_typedState);

    internal static (HttpClient Http, DecisionClient Client) CreateClient(string responseJson)
        => CreateClient(responseJson, loggerFactory: null);

    internal static (HttpClient Http, DecisionClient Client) CreateClient(string responseJson, ILoggerFactory? loggerFactory)
        => CreateClient(new CannedHandler(HttpStatusCode.OK, responseJson), loggerFactory);

    internal static (HttpClient Http, DecisionClient Client) CreateClient(HttpMessageHandler handler, ILoggerFactory? loggerFactory)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        // Default retry options: the handler always returns 200, so no retry ever fires, and
        // the benchmark measures the resilience proxy's per-call overhead that users get by default.
        // A null factory builds exactly the client the two-argument constructor builds.
        var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "bench" }, loggerFactory);
        return (http, client);
    }

    /// <summary>Answers every request with one canned response, so the benchmark needs no network.</summary>
    internal sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
    }
}
