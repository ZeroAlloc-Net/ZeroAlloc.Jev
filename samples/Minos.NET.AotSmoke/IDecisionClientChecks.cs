using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="IDecisionClient"/>'s members called through the interface, on a <see cref="DecisionClient"/> resolved as an
/// <see cref="IDecisionClient"/>, and <see cref="DecisionClientExtensions"/> over <see cref="ExtensionFallbackClient"/>,
/// a client that is not a <see cref="DecisionClient"/>, so each typed and built-set check proves the extension path.
/// </summary>
internal static class IDecisionClientChecks
{
    [Covers("Minos.IDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.IDecisionClient.GetService(System.Type! serviceType, object? serviceKey = null) -> object?")]
    [Covers("Minos.DecisionRequest.DecisionRequest(Minos.QuestionSetDefinition! Definition, Minos.DecisionContent State) -> void")]
    [Covers("static Minos.DecisionClientExtensions.GetService<TService>(this Minos.IDecisionClient! client, object? serviceKey = null) -> TService?")]
    [Covers("Minos.DecisionClient.EvaluateAsync(Minos.DecisionRequest! request) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.DecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.DecisionClient.GetService(System.Type! serviceType, object? serviceKey = null) -> object?")]
    public static async Task NeutralCallAndServicesRunThroughTheInterface()
    {
        var client = ResolveOver(Program.NoulResponse, out var provider);
        using var cancellation = new CancellationTokenSource();
        var request = new DecisionRequest(UrgencyDefinition(), SmokeAnswers.State);

        var evaluated = await client.EvaluateAsync(request, cancellation.Token).ConfigureAwait(false);
        var metadata = client.GetService(typeof(DecisionClientMetadata)) as DecisionClientMetadata;
        var facade = client.GetService<DecisionClient>();
        var keyed = client.GetService<DecisionClientMetadata>(serviceKey: "other");
        await provider.DisposeAsync().ConfigureAwait(false);

        using var http = Program.Http(HttpStatusCode.OK, Program.NoulResponse);
        using var direct = new DecisionClient(http, Program.Options());
        var plain = await direct.EvaluateAsync(request).ConfigureAwait(false);
        var cancellable = await direct.EvaluateAsync(request, cancellation.Token).ConfigureAwait(false);
        var self = direct.GetService(typeof(DecisionClient), serviceKey: null);

        Program.Check(
            evaluated.IsSuccess && evaluated.Value.Answers[0].Value == 0.95 && ReferenceEquals(evaluated.Value.Definition, request.Definition),
            "IDecisionClient.EvaluateAsync(DecisionRequest) evaluates through the interface under Native AOT");
        Program.Check(
            metadata is { ProviderName: "typesafe", Endpoint: not null } && facade is not null && keyed is null,
            "IDecisionClient.GetService finds the metadata and the DecisionClient, and nothing for a key, under Native AOT");
        Program.Check(
            plain.IsSuccess && cancellable.IsSuccess && ReferenceEquals(self, direct),
            "DecisionClient's neutral EvaluateAsync pair and GetService run under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, string! state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, string! state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, System.Text.Json.JsonElement state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, System.Text.Json.JsonElement state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateUtf8Async<T>(this Minos.IDecisionClient! client, System.ReadOnlyMemory<byte> utf8JsonState) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateUtf8Async<T>(this Minos.IDecisionClient! client, System.ReadOnlyMemory<byte> utf8JsonState, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    public static async Task TypedExtensionsSendTheStateAndCreateAnswers()
    {
        var fallback = new ExtensionFallbackClient(await ResponseFor(Program.TriageResponse, SmokeTriage.Definition).ConfigureAwait(false));
        IDecisionClient client = fallback;
        using var state = JsonDocument.Parse(SmokeAnswers.JsonState);
        using var cancellation = new CancellationTokenSource();
        var text = DecisionContent.FromString(SmokeAnswers.State);
        var json = DecisionContent.FromJson(state.RootElement);
        var utf8 = Encoding.UTF8.GetBytes(SmokeAnswers.JsonState);

        var fromText = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);
        var sentText = Sent(fallback, text, CancellationToken.None);
        var fromCancellableText = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State, cancellation.Token).ConfigureAwait(false);
        var sentCancellableText = Sent(fallback, text, cancellation.Token);
        var fromJson = await client.EvaluateAsync<SmokeTriage>(state.RootElement).ConfigureAwait(false);
        var sentJson = Sent(fallback, json, CancellationToken.None);
        var fromCancellableJson = await client.EvaluateAsync<SmokeTriage>(state.RootElement, cancellation.Token).ConfigureAwait(false);
        var sentCancellableJson = Sent(fallback, json, cancellation.Token);
        var fromUtf8 = await client.EvaluateUtf8Async<SmokeTriage>(utf8).ConfigureAwait(false);
        var sentUtf8 = Sent(fallback, json, CancellationToken.None);
        var fromCancellableUtf8 = await client.EvaluateUtf8Async<SmokeTriage>(utf8, cancellation.Token).ConfigureAwait(false);
        var sentCancellableUtf8 = Sent(fallback, json, cancellation.Token);

        Program.Check(
            sentText && sentCancellableText && sentJson && sentCancellableJson && sentUtf8 && sentCancellableUtf8,
            "the typed extensions send the text state as text and the JSON states as the same JSON, with the caller's token, under Native AOT");
        Program.Check(
            SmokeAnswers.IsTriage(fromText)
                && SmokeAnswers.IsTriage(fromCancellableText)
                && SmokeAnswers.IsTriage(fromJson)
                && SmokeAnswers.IsTriage(fromCancellableJson)
                && SmokeAnswers.IsTriage(fromUtf8)
                && SmokeAnswers.IsTriage(fromCancellableUtf8),
            "the typed extensions over text, JSON and UTF-8 states create typed answers under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T, TState>(this Minos.IDecisionClient! client, TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T, TState>(this Minos.IDecisionClient! client, TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    public static async Task TypedStateExtensionsSendTheStateAndCreateAnswers()
    {
        var fallback = new ExtensionFallbackClient(await ResponseFor(Program.CredentialsResponse, SmokeStateTriage.Definition).ConfigureAwait(false));
        IDecisionClient client = fallback;
        using var cancellation = new CancellationTokenSource();
        var state = new SmokeState("Payouts failing", SmokeAnswers.State);
        var expected = DecisionContent.FromValue(state, SmokeStateJsonContext.Default.SmokeState);

        var result = await client
            .EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState)
            .ConfigureAwait(false);
        var sent = Sent(fallback, expected, CancellationToken.None);
        var cancellable = await client
            .EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState, cancellation.Token)
            .ConfigureAwait(false);
        var sentCancellable = Sent(fallback, expected, cancellation.Token);

        Program.Check(
            sent && sentCancellable,
            "the EvaluateAsync<T, TState> extensions send the state serialized through its JsonTypeInfo under Native AOT");
        Program.Check(
            result.IsSuccess && !result.Value.RequestsCredentials.Value && cancellable.IsSuccess && !cancellable.Value.RequestsCredentials.Value,
            "the EvaluateAsync<T, TState> extensions create typed answers under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync(this Minos.IDecisionClient! client, Minos.QuestionSet! questionSet, Minos.DecisionContent state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.Answers!, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync(this Minos.IDecisionClient! client, Minos.QuestionSet! questionSet, Minos.DecisionContent state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.Answers!, Minos.DecisionError!>>")]
    public static async Task BuiltSetExtensionsSendTheStateAndReadAnswers()
    {
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        var fallback = new ExtensionFallbackClient(await ResponseFor(SmokeBuiltSet.ResponseJson, set.Definition).ConfigureAwait(false));
        IDecisionClient client = fallback;
        using var cancellation = new CancellationTokenSource();
        var expected = DecisionContent.FromString(SmokeAnswers.State);

        var result = await client.EvaluateAsync(set, SmokeAnswers.State).ConfigureAwait(false);
        var sent = Sent(fallback, expected, CancellationToken.None);
        var cancellable = await client.EvaluateAsync(set, SmokeAnswers.State, cancellation.Token).ConfigureAwait(false);
        var sentCancellable = Sent(fallback, expected, cancellation.Token);

        Program.Check(
            sent && sentCancellable,
            "the built-set extensions send the state under Native AOT");
        Program.Check(
            result.IsSuccess
                && cancellable.IsSuccess
                && !result.Value.Get(credentials).Value
                && result.Value.Get(team).Value == Team.Account
                && string.Equals(cancellable.Value.Get(product).Value, "pro-plan", StringComparison.Ordinal)
                && cancellable.Value.Get(urgency).Value == Urgency.High,
            "the built-set extensions read the answers under Native AOT");
        Program.Check(
            await SmokeAssert.ThrowsAsync<ArgumentException>(() => client.EvaluateAsync(set, default(DecisionContent)).AsTask()).ConfigureAwait(false),
            "the built-set extension rejects uninitialized content under Native AOT");
    }

    // The one Noul question Program.NoulResponse answers.
    private static QuestionSetDefinition UrgencyDefinition()
        => new(QuestionDefinition.Noul("is_urgent", DecisionContent.FromString("Does this convey urgency?")));

    // Whether the last request the fallback client received carried expected as its state, with no model, and token.
    private static bool Sent(ExtensionFallbackClient fallback, DecisionContent expected, CancellationToken token)
        => fallback.LastRequest is { Model: null } request && request.State.Equals(expected) && fallback.LastToken == token;

    // A client resolved as the interface, as an app gets it, so each call binds to IDecisionClient's member.
    private static IDecisionClient ResolveOver(string responseJson, out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services
            .AddDecisionClient(options =>
            {
                options.ApiKey = "smoke-key";
                options.BaseAddress = new Uri("https://example.test/api/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, responseJson));
        provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDecisionClient>();
    }

    // The response a real client reads from canned JSON for exactly this definition, so the fallback client answers as
    // the service would, for the definition the extension asks about.
    private static async Task<DecisionResponse> ResponseFor(string responseJson, QuestionSetDefinition definition)
    {
        using var http = Program.Http(HttpStatusCode.OK, responseJson);
        using var client = new DecisionClient(http, Program.Options());
        var result = await client.EvaluateAsync(new DecisionRequest(definition, SmokeAnswers.State)).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException("The canned response did not parse: " + result.Error.Message);
    }
}
