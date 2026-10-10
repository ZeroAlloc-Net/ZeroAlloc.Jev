using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="DecisionClient"/>'s constructors, <see cref="DecisionClient.Dispose"/> and the evaluation overloads the other
/// checks leave out, each called on a <see cref="DecisionClient"/> under Native AOT.
/// </summary>
internal static class DecisionClientChecks
{

    [Covers("Minos.DecisionClient.DecisionClient() -> void")]
    [Covers("Minos.DecisionClient.DecisionClient(Minos.DecisionClientOptions? options) -> void")]
    [Covers("Minos.DecisionClient.DecisionClient(Minos.DecisionClientOptions? options, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory) -> void")]
    [Covers("Minos.DecisionClient.Dispose() -> void")]
    public static async Task ClientsThatOwnTheirHttpClientRefuseCallsAfterDispose()
    {
        using (SmokeAssert.NoApiKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<InvalidOperationException>(() => new DecisionClient().Dispose()),
                "new DecisionClient() without TYPESAFE_API_KEY throws under Native AOT");
        }

        DecisionClient fromEnvironment;
        using (SmokeAssert.TypeSafeEnvironment())
        {
            fromEnvironment = new DecisionClient();
        }

        var fromOptions = new DecisionClient(Program.Options());
        var withLogging = new DecisionClient(Program.Options(), NullLoggerFactory.Instance);
        fromEnvironment.Dispose();
        fromOptions.Dispose();
        withLogging.Dispose();
        withLogging.Dispose();

        Program.Check(
            await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => fromEnvironment.EvaluateAsync(Program.Request()).AsTask()).ConfigureAwait(false)
                && await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => fromOptions.EvaluateAsync(Program.Request()).AsTask()).ConfigureAwait(false)
                && await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => withLogging.EvaluateAsync(Program.Request()).AsTask()).ConfigureAwait(false),
            "a DecisionClient that owns its HttpClient is built from the environment or options, disposes twice and then refuses calls, under Native AOT");
    }

    [Covers("Minos.DecisionClient.DecisionClient(System.Net.Http.HttpClient! httpClient) -> void")]
    public static async Task ClientOverAnHttpClientReadsTheKeyFromTheEnvironment()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.NoulResponse);
        DecisionClient client;
        using (SmokeAssert.TypeSafeEnvironment())
        {
            client = new DecisionClient(http);
        }

        using (client)
        {
            var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);

            Program.Check(
                result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer { Noul: 0.95 },
                "new DecisionClient(httpClient) takes its key from TYPESAFE_API_KEY and evaluates under Native AOT");
        }
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync(this Minos.IDecisionClient! client, Minos.QuestionSet! questionSet, Minos.DecisionContent state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.Answers!, Minos.DecisionError!>>")]
    public static async Task BuiltSetEvaluatesWithACancellationToken()
    {
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        using var http = Program.Http(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson);
        using var client = new DecisionClient(http, Program.Options());
        using var cancellation = new CancellationTokenSource();

        var result = await client.EvaluateAsync(set, SmokeAnswers.State, cancellation.Token).ConfigureAwait(false);

        Program.Check(
            result.IsSuccess
                && !result.Value.Get(credentials).Value
                && result.Value.Get(team).Value == Team.Account
                && string.Equals(result.Value.Get(product).Value, "pro-plan", StringComparison.Ordinal)
                && result.Value.Get(urgency).Value == Urgency.High,
            "EvaluateAsync(set, state, cancellationToken) on a DecisionClient evaluates a built set under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, string! state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    public static async Task TypedTextStateEvaluatesWithACancellationToken()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.TriageResponse);
        using var client = new DecisionClient(http, Program.Options());
        using var cancellation = new CancellationTokenSource();

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State, cancellation.Token).ConfigureAwait(false);

        Program.Check(SmokeAnswers.IsTriage(result), "EvaluateAsync<T>(string, cancellationToken) on a DecisionClient parses typed answers under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, System.Text.Json.JsonElement state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, System.Text.Json.JsonElement state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    public static async Task TypedJsonStateEvaluates()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.TriageResponse);
        using var client = new DecisionClient(http, Program.Options());
        using var state = JsonDocument.Parse(SmokeAnswers.JsonState);
        using var cancellation = new CancellationTokenSource();

        var result = await client.EvaluateAsync<SmokeTriage>(state.RootElement).ConfigureAwait(false);
        var cancellable = await client.EvaluateAsync<SmokeTriage>(state.RootElement, cancellation.Token).ConfigureAwait(false);

        Program.Check(
            SmokeAnswers.IsTriage(result) && SmokeAnswers.IsTriage(cancellable),
            "EvaluateAsync<T>(JsonElement) and its cancellable overload on a DecisionClient parse typed answers under Native AOT");
        Program.Check(
            await SmokeAssert.ThrowsAsync<ArgumentException>(() => client.EvaluateAsync<SmokeTriage>(default(JsonElement)).AsTask()).ConfigureAwait(false),
            "EvaluateAsync<T>(JsonElement) on a DecisionClient rejects an undefined JSON state under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateUtf8Async<T>(this Minos.IDecisionClient! client, System.ReadOnlyMemory<byte> utf8JsonState) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    public static async Task TypedUtf8StateEvaluates()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.TriageResponse);
        using var client = new DecisionClient(http, Program.Options());
        var state = Encoding.UTF8.GetBytes(SmokeAnswers.JsonState);

        var result = await client.EvaluateUtf8Async<SmokeTriage>(state).ConfigureAwait(false);

        Program.Check(SmokeAnswers.IsTriage(result), "EvaluateUtf8Async<T> on a DecisionClient parses typed answers from a UTF-8 JSON state under Native AOT");
        Program.Check(
            await SmokeAssert.ThrowsAsync<ArgumentException>(() => client.EvaluateUtf8Async<SmokeTriage>("{} {}"u8.ToArray()).AsTask()).ConfigureAwait(false),
            "EvaluateUtf8Async<T> on a DecisionClient rejects a state that is not one JSON value under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T, TState>(this Minos.IDecisionClient! client, TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    public static async Task TypedStateEvaluatesWithACancellationToken()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.CredentialsResponse);
        using var client = new DecisionClient(http, Program.Options());
        using var cancellation = new CancellationTokenSource();
        var state = new SmokeState("Payouts failing", SmokeAnswers.State);

        var result = await client
            .EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState, cancellation.Token)
            .ConfigureAwait(false);

        Program.Check(
            result.IsSuccess && !result.Value.RequestsCredentials.Value,
            "EvaluateAsync<T, TState>(state, stateTypeInfo, cancellationToken) on a DecisionClient parses typed answers under Native AOT");
    }
}
