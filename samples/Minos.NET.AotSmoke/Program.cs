using System.Net;
using System.Text;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary>
/// Native AOT smoke test: publishes with PublishAot and exercises the real client and generated code paths over a
/// canned handler. Exits 0 when every check passes, 1 otherwise.
/// </summary>
internal static class Program
{
    internal const string NoulResponse = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    internal const string ModelsResponse = """{"models":[{"name":"jev-latest","description":"The most recent stable, official release.","release_date":"2026-09-15"}]}""";
    internal const string ValidationResponse = """{"detail":"questions.is_urgent.instructions is required"}""";
    internal const string TriageAnswers = """{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}}""";
    internal const string TriageResponse = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    internal const string StructuredResponse = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    internal const string CredentialsResponse = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    private static int failures;

    private static async Task<int> Main()
    {
        await EvaluateParsesAnswers().ConfigureAwait(false);
        await ValidationErrorCarriesDetail().ConfigureAwait(false);
        await ListModelsReturnsModels().ConfigureAwait(false);
        await MalformedBodyIsInvalidResponse().ConfigureAwait(false);
        await OpenRouterModelListingIsUnsupported().ConfigureAwait(false);
        await OverloadedThenSuccessIsRetried().ConfigureAwait(false);
        await GeneratedQuestionSetRoundTrips().ConfigureAwait(false);
        await StructuredQuestionSetRoundTrips().ConfigureAwait(false);
        await TypedEvaluateAsyncParsesAnswers().ConfigureAwait(false);
        await TypedEvaluateAsyncWithTStateParsesAnswers().ConfigureAwait(false);
        await HandBuiltResponseFeedsTheTypedExtension().ConfigureAwait(false);
        SystemOneResponseModelConstructs();
        await BuiltQuestionSetEvaluates().ConfigureAwait(false);
        await BuiltEnumChoiceReadsTheFieldsInDeclarationOrder().ConfigureAwait(false);
        await LoggingChecks.RetriedEvaluationLogsTheRetryAndTheSuccess().ConfigureAwait(false);
        await LoggingChecks.TypedEvaluationLogsItsQuestionCount().ConfigureAwait(false);
        await LoggingChecks.FailedEvaluationLogsTheLibraryMessageOnly().ConfigureAwait(false);
        await LoggingChecks.ModelListingLogsTheModelCount().ConfigureAwait(false);
        await DependencyInjectionChecks.DefaultAndKeyedClientsEvaluate().ConfigureAwait(false);
        await DependencyInjectionChecks.ClientsBoundFromConfigurationEvaluate().ConfigureAwait(false);
        DependencyInjectionChecks.InvalidConfigurationFailsValidation();
        await TelemetryChecks.RetriedEvaluationIsOneSpanOverTwoAttempts().ConfigureAwait(false);
        await TelemetryChecks.TypedEvaluationRecordsItsMetrics().ConfigureAwait(false);
        await TelemetryChecks.FailedEvaluationIsAnError().ConfigureAwait(false);
        await TelemetryChecks.CancelledEvaluationSetsErrorTypeWithoutItsMessage().ConfigureAwait(false);
        await DependencyInjectionChecks.ClientsConfiguredFromTheEnvironmentEvaluate().ConfigureAwait(false);

        // One group per public type, so together with the checks above every public entry point runs under Native AOT.
        await DecisionClientChecks.ClientsThatOwnTheirHttpClientRefuseCallsAfterDispose().ConfigureAwait(false);
        await DecisionClientChecks.ClientOverAnHttpClientReadsTheKeyFromTheEnvironment().ConfigureAwait(false);
        await DecisionClientChecks.BuiltSetEvaluatesWithACancellationToken().ConfigureAwait(false);
        await DecisionClientChecks.TypedTextStateEvaluatesWithACancellationToken().ConfigureAwait(false);
        await DecisionClientChecks.TypedJsonStateEvaluates().ConfigureAwait(false);
        await DecisionClientChecks.TypedUtf8StateEvaluates().ConfigureAwait(false);
        await DecisionClientChecks.TypedStateEvaluatesWithACancellationToken().ConfigureAwait(false);
        await IDecisionClientChecks.NeutralCallAndServicesRunThroughTheInterface().ConfigureAwait(false);
        await IDecisionClientChecks.TypedExtensionsSendTheStateAndCreateAnswers().ConfigureAwait(false);
        await IDecisionClientChecks.TypedStateExtensionsSendTheStateAndCreateAnswers().ConfigureAwait(false);
        await IDecisionClientChecks.BuiltSetExtensionsSendTheStateAndReadAnswers().ConfigureAwait(false);
        DecisionClientOptionsChecks.ValidateAcceptsValidOptionsAndRejectsInvalidOnes();
        DecisionContentChecks.TextContentRoundTrips();
        DecisionContentChecks.JsonContentRoundTrips();
        await CriterionChecks.NotForTextsAreSentWithTheDescription().ConfigureAwait(false);
        await QuestionSetBuilderChecks.NoulCriteriaAndUndescribedKeyedOptionsAreSent().ConfigureAwait(false);
        await QuestionHandleChecks.DefaultHandlesAreRejected().ConfigureAwait(false);
        DefinitionChecks.QuestionSetDefinitionKeepsItsQuestions();
        DefinitionChecks.EmptyAnswerSlotsHoldNoAnswers();
        DefinitionChecks.AnswerSlotsRefuseAnIndexOutOfRange();
        await NoulChecks.EmptyNoulIsFalse().ConfigureAwait(false);
        await ChoiceChecks.ChoiceIsRebuiltAndCompared().ConfigureAwait(false);
        await ScoreChecks.ScoreIsRebuiltAndCompared().ConfigureAwait(false);
        await ProbabilityMapChecks.ProbabilityMapEnumeratesAndCompares().ConfigureAwait(false);
        await KeyedChoiceChecks.KeyedChoiceIsRebuiltAndCompared().ConfigureAwait(false);
        await KeyedScoreChecks.KeyedScoreIsRebuiltAndCompared().ConfigureAwait(false);
        await KeyedProbabilityMapChecks.KeyedProbabilityMapEnumeratesAndCompares().ConfigureAwait(false);
        ConfidenceThresholdsChecks.DefaultThresholdsEqualTheirExplicitValues();
        DecisionErrorChecks.HandBuiltErrorCarriesItsKindAndMessage();
        QuestionFailureChecks.HandBuiltFailureEqualsTheReportedOne();
        await SystemOneModelChecks.HandBuiltQuestionsAreSent().ConfigureAwait(false);
        await SystemOneModelChecks.HandBuiltModelCardEqualsTheListedOne().ConfigureAwait(false);
        QuestionAttributeChecks.AttributesKeepWhatTheyAreGiven();
        await MissingAnswerChecks.MissingAnswerNamesTheQuestion().ConfigureAwait(false);
        DecisionOptionSetChecks.HandWrittenOptionSetMapsOptions();
        IQuestionSetChecks.CreateRunsThroughTheInterface();

        await AllocationChecks.AnswerSlotAccessors().ConfigureAwait(false);
        await AllocationChecks.GeneratedCreate().ConfigureAwait(false);
        AllocationChecks.EvaluateRoundTrip();
        AllocationChecks.TypedEvaluateRoundTrip();
        AllocationChecks.EvaluateRoundTripWithNullLoggerFactory();
        AllocationChecks.TypedEvaluateRoundTripWithEveryLevelFiltered();
        AllocationChecks.EvaluateRoundTripWithDiscardingLogger();
        AllocationChecks.TypedEvaluateRoundTripWithDiscardingLogger();
        await AllocationChecks.DisabledLoggerAddsNothingWhereAnEnabledOneDoes().ConfigureAwait(false);
        AllocationChecks.ContentFromValue();
        AllocationChecks.ContentFromUtf8Json();
        AllocationChecks.BuildQuestionSet();
        AllocationChecks.EvaluateBuiltSetRoundTrip();
        AllocationChecks.AnswersGet();
        AllocationChecks.PatternHelpers();
        AllocationChecks.NoulEquals();
        AllocationChecks.EvaluateRoundTripThroughDependencyInjection();
        AllocationChecks.EvaluateRoundTripThroughBoundConfiguration();
        await AllocationChecks.TelemetryOffAsynchronousTypedEvaluation().ConfigureAwait(false);
        AllocationChecks.EvaluateRoundTripWhileListening();
        AllocationChecks.TypedEvaluateRoundTripWhileListening();
        AllocationChecks.EvaluateBuiltSetRoundTripWhileListening();

        Console.WriteLine(failures == 0 ? "AOT smoke: all checks passed" : "AOT smoke: " + failures + " check(s) failed");
        return failures == 0 ? 0 : 1;
    }

    [Covers("Minos.DecisionClient.DecisionClient(System.Net.Http.HttpClient! httpClient, Minos.DecisionClientOptions? options) -> void")]
    [Covers("Minos.DecisionClient.EvaluateAsync(Minos.SystemOneRequest! request) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.SystemOneResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.DecisionClientOptions.DecisionClientOptions() -> void")]
    [Covers("Minos.SystemOneRequest.SystemOneRequest() -> void")]
    [Covers("Minos.NoulQuestion.NoulQuestion() -> void")]
    private static async Task EvaluateParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, NoulResponse);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer { Noul: 0.95 }, "EvaluateAsync parses a Noul answer");
    }

    private static async Task ValidationErrorCarriesDetail()
    {
        using var http = Http(HttpStatusCode.UnprocessableEntity, ValidationResponse);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(
            result.IsFailure
                && result.Error.Kind == DecisionErrorKind.Validation
                && result.Error.Detail is { } detail
                && string.Equals(detail.GetProperty("detail").GetString(), "questions.is_urgent.instructions is required", StringComparison.Ordinal),
            "a 422 maps to Validation with its JSON detail");
    }

    [Covers("Minos.DecisionClient.ListModelsAsync(System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.ModelList!, Minos.DecisionError!>>")]
    private static async Task ListModelsReturnsModels()
    {
        using var http = Http(HttpStatusCode.OK, ModelsResponse);
        using var client = new DecisionClient(http, Options());

        var result = await client.ListModelsAsync().ConfigureAwait(false);

        Check(
            result.IsSuccess && result.Value.Models.Count == 1 && string.Equals(result.Value.Models[0].Name, "jev-latest", StringComparison.Ordinal),
            "ListModelsAsync parses the models");
    }

    private static async Task MalformedBodyIsInvalidResponse()
    {
        using var http = Http(HttpStatusCode.OK, "not json");
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsFailure && result.Error.Kind == DecisionErrorKind.InvalidResponse, "a malformed 2xx body maps to InvalidResponse");
    }

    private static async Task OpenRouterModelListingIsUnsupported()
    {
        using var http = Http(HttpStatusCode.OK, ModelsResponse);
        using var client = new DecisionClient(http, Options(DecisionProvider.OpenRouter));

        var result = await client.ListModelsAsync().ConfigureAwait(false);

        Check(result.IsFailure && result.Error.Kind == DecisionErrorKind.Unsupported, "model listing on OpenRouter is Unsupported");
    }

    private static async Task OverloadedThenSuccessIsRetried()
    {
        var handler = new SequenceHandler(NoulResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key", InitialBackoff = TimeSpan.FromMilliseconds(10) });

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsSuccess && handler.Calls == 2, "a 503 is retried through the resilience proxy");
    }

    private static async Task GeneratedQuestionSetRoundTrips()
    {
        using var questions = await CapturingHandler.QuestionsSentAsync<SmokeTriage>(TriageResponse).ConfigureAwait(false);
        Check(
            string.Equals(
                questions.RootElement.GetProperty("team").GetProperty("criteria").GetProperty("account").GetString(),
                "Login, profile, permissions",
                StringComparison.Ordinal),
            "the questions sent for a generated set carry the Choice criteria");
    }

    [Covers("static Minos.Criterion.Json(Minos.DecisionContent json) -> Minos.Criterion!")]
    private static async Task StructuredQuestionSetRoundTrips()
    {
        using var generated = await CapturingHandler.QuestionsSentAsync<SmokeStructured>(StructuredResponse).ConfigureAwait(false);
        Check(
            string.Equals(
                generated.RootElement.GetProperty("team").GetProperty("criteria").GetProperty("billing").GetProperty("not_for")[0].GetString(),
                "How much is Pro?",
                StringComparison.Ordinal),
            "Examples and NotFor are sent as a criterion object");

        using var built = await CapturingHandler.QuestionsSentAsync(SmokeBuiltSet.Structured(), CredentialsResponse).ConfigureAwait(false);
        var root = built.RootElement;
        Check(
            root.GetProperty("requests_credentials").GetProperty("instructions").GetProperty("policy").GetProperty("strict").GetBoolean(),
            "DecisionContent instructions are sent as a JSON object");
        Check(
            string.Equals(
                root.GetProperty("team").GetProperty("criteria").GetProperty("account").GetProperty("owner").GetString(),
                "identity",
                StringComparison.Ordinal),
            "a Criterion.Json description is sent as JSON");
    }

    [Covers("Minos.DecisionClient.EvaluateAsync<T>(string! state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    private static async Task TypedEvaluateAsyncParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, TriageResponse);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);

        Check(
            result.IsSuccess
                && !result.Value.RequestsCredentials.Value
                && result.Value.Team.Value == Team.Account
                && result.Value.Urgency.Value == Urgency.High,
            "EvaluateAsync<T>(string) parses typed answers over the raw, pooled-buffer path");
    }

    [Covers("Minos.DecisionClient.EvaluateAsync<T, TState>(TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    private static async Task TypedEvaluateAsyncWithTStateParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, CredentialsResponse);
        using var client = new DecisionClient(http, Options());
        var state = new SmokeState("Payouts failing", SmokeAnswers.State);

        var result = await client.EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState).ConfigureAwait(false);

        Check(
            result.IsSuccess && !result.Value.RequestsCredentials.Value,
            "EvaluateAsync<T, TState>(state, stateTypeInfo) parses typed answers over the raw, pooled-buffer path");
    }

    [Covers("Minos.DecisionResponse.DecisionResponse(Minos.QuestionSetDefinition! definition, System.Collections.Generic.IReadOnlyList<Minos.QuestionAnswer>! answers, string? model = null, Minos.DecisionUsage? usage = null) -> void")]
    [Covers("static Minos.QuestionAnswer.Noul(double value) -> Minos.QuestionAnswer")]
    [Covers("static Minos.QuestionAnswer.Choice(int chosenIndex, double confidence, double[]! probabilities) -> Minos.QuestionAnswer")]
    [Covers("static Minos.QuestionAnswer.Score(int level, double value, double confidence, double[]! probabilities) -> Minos.QuestionAnswer")]
    [Covers("Minos.DecisionUsage.DecisionUsage() -> void")]
    private static async Task HandBuiltResponseFeedsTheTypedExtension()
    {
        // A fake's response, built with the public constructor and the QuestionAnswer factories, read by the generated
        // SmokeTriage.Create through the typed extension, as a test fake would be, under Native AOT.
        var response = new DecisionResponse(
            SmokeTriage.Definition,
            [QuestionAnswer.Noul(0.1), QuestionAnswer.Choice(1, 0.7, [0.2, 0.8]), QuestionAnswer.Score(2, 1.9, 0.8, [0.0, 0.1, 0.9])],
            "minos-smoke",
            new DecisionUsage { InputTokens = 296, OutputTokens = 20 });
        using var client = new ExtensionFallbackClient(response);

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);

        Check(
            SmokeAnswers.IsTriage(result) && result.Value.Team.Confidence == 0.7 && result.Value.Urgency.Value == Urgency.High,
            "a hand-built DecisionResponse creates typed answers through the typed extension under Native AOT");
    }

    [Covers("Minos.SystemOneResponse.SystemOneResponse() -> void")]
    [Covers("Minos.NoulAnswer.NoulAnswer() -> void")]
    [Covers("Minos.ChoiceAnswer.ChoiceAnswer() -> void")]
    [Covers("Minos.ScoreAnswer.ScoreAnswer() -> void")]
    private static void SystemOneResponseModelConstructs()
    {
        // The raw System One response model, as a caller of DecisionClient's raw EvaluateAsync builds one in a test.
        var response = new SystemOneResponse
        {
            Model = "minos-smoke",
            Answers = new Dictionary<string, Answer>(StringComparer.Ordinal)
            {
                ["requests_credentials"] = new NoulAnswer { Noul = 0.1 },
                ["team"] = new ChoiceAnswer
                {
                    Choice = "account",
                    Probabilities = new Dictionary<string, double>(StringComparer.Ordinal) { ["billing"] = 0.2, ["account"] = 0.8 },
                    Confidence = 0.7,
                },
                ["urgency"] = new ScoreAnswer
                {
                    Score = 1.9,
                    Legend = new Dictionary<string, string>(StringComparer.Ordinal) { ["0"] = "Can wait", ["1"] = "This week", ["2"] = "Today" },
                    Probabilities = new Dictionary<string, double>(StringComparer.Ordinal) { ["0"] = 0.0, ["1"] = 0.1, ["2"] = 0.9 },
                    Confidence = 0.8,
                },
            },
            Usage = new DecisionUsage { InputTokens = 296, OutputTokens = 20 },
        };

        Check(
            response.Answers["requests_credentials"] is NoulAnswer { Noul: 0.1 }
                && response.Answers["team"] is ChoiceAnswer { Choice: "account" }
                && response.Answers["urgency"] is ScoreAnswer { Score: 1.9 },
            "the raw System One response model constructs under Native AOT");
    }

    [Covers("static Minos.QuestionSet.CreateBuilder() -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.QuestionSetBuilder.Build() -> ZeroAlloc.Results.Result<Minos.QuestionSet!, Minos.DecisionError!>")]
    [Covers("Minos.QuestionSetBuilder.Noul(string! key, Minos.DecisionContent instructions, out Minos.NoulHandle question) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.QuestionSetBuilder.Choice<T>(string! key, Minos.DecisionContent instructions, out Minos.ChoiceHandle<T> question, System.Action<Minos.ChoiceOptionsBuilder<T>!>! configure) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.QuestionSetBuilder.Choice(string! key, Minos.DecisionContent instructions, out Minos.KeyedChoiceHandle question, System.Action<Minos.KeyedChoiceOptionsBuilder!>! configure) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.QuestionSetBuilder.Score<T>(string! key, Minos.DecisionContent instructions, out Minos.ScoreHandle<T> question, System.Action<Minos.ScoreLevelsBuilder<T>!>! configure) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.ChoiceOptionsBuilder<T>.Describe(T option, Minos.Criterion! criterion) -> Minos.ChoiceOptionsBuilder<T>!")]
    [Covers("Minos.KeyedChoiceOptionsBuilder.Option(string! key, Minos.Criterion! criterion) -> Minos.KeyedChoiceOptionsBuilder!")]
    [Covers("Minos.ScoreLevelsBuilder<T>.Level(T level, Minos.Criterion! criterion) -> Minos.ScoreLevelsBuilder<T>!")]
    [Covers("static Minos.Criterion.Text(string! description) -> Minos.Criterion!")]
    [Covers("Minos.Criterion.WithExamples(params System.ReadOnlySpan<string?> examples) -> Minos.Criterion!")]
    [Covers("Minos.DecisionClient.EvaluateAsync(Minos.QuestionSet! questionSet, Minos.DecisionContent state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.Answers!, Minos.DecisionError!>>")]
    [Covers("Minos.Answers.Get(Minos.NoulHandle question) -> Minos.Noul")]
    [Covers("Minos.Answers.Get(Minos.KeyedChoiceHandle question) -> Minos.KeyedChoice")]
    [Covers("Minos.Answers.Get<T>(Minos.ChoiceHandle<T> question) -> Minos.Choice<T>")]
    [Covers("Minos.Answers.Get<T>(Minos.ScoreHandle<T> question) -> Minos.Score<T>")]
    private static async Task BuiltQuestionSetEvaluates()
    {
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        Check(set.Warnings.Count == 0, "a question set built at run time passes its rules");

        using var http = Http(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(set, SmokeAnswers.State).ConfigureAwait(false);

        Check(
            result.IsSuccess
                && !result.Value.Get(credentials).Value
                && result.Value.Get(team).Value == Team.Account
                && string.Equals(result.Value.Get(product).Value, "pro-plan", StringComparison.Ordinal)
                && result.Value.Get(urgency).Value == Urgency.High,
            "a built question set evaluates over the raw, pooled-buffer path");

        var invalid = QuestionSet.CreateBuilder().Choice("empty", "Which one?", out _, options => { }).Build();
        Check(
            invalid.IsFailure
                && invalid.Error.Kind == DecisionErrorKind.InvalidQuestions
                && string.Equals(invalid.Error.Failures[0].Rule, "MIN001", StringComparison.Ordinal),
            "a built question set that breaks a rule fails with its MIN id");
    }

    [Covers("Minos.QuestionSetBuilder.Choice<T>(string! key, Minos.DecisionContent instructions, out Minos.ChoiceHandle<T> question) -> Minos.QuestionSetBuilder!")]
    private static async Task BuiltEnumChoiceReadsTheFieldsInDeclarationOrder()
    {
        var set = SmokeBuiltSet.AliasedChoice(out var channel);
        using var sent = await CapturingHandler.QuestionsSentAsync(set, SmokeBuiltSet.AliasedChoiceResponseJson).ConfigureAwait(false);
        Check(
            string.Equals(sent.RootElement.GetRawText(), SmokeBuiltSet.AliasedChoiceQuestions, StringComparison.Ordinal),
            "a built enum Choice sends its options in declaration order, the alias skipped, under Native AOT");

        using var http = Http(HttpStatusCode.OK, SmokeBuiltSet.AliasedChoiceResponseJson);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(set, "Sent from my phone's mail app.").ConfigureAwait(false);

        Check(
            result.IsSuccess
                && result.Value.Get(channel).Value == Channel.Email
                && Math.Abs(result.Value.Get(channel).Probabilities[Channel.Mail] - 0.8) < 1e-12,
            "a built enum Choice keys an aliased value by its first declared name, email, under Native AOT");
    }

    internal static HttpClient Http(HttpStatusCode status, string body)
        => new(new CannedHandler(status, body)) { BaseAddress = new Uri("https://example.test/api/") };

    internal static DecisionClientOptions Options(DecisionProvider provider = DecisionProvider.TypeSafe)
        => new() { ApiKey = "smoke-key", Provider = provider };

    internal static SystemOneRequest Request() => new()
    {
        State = SmokeAnswers.State,
        Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
        {
            ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
        },
    };

    internal static void Check(bool passed, string description)
    {
        Console.WriteLine((passed ? "PASS " : "FAIL ") + description);
        if (!passed)
        {
            failures++;
        }
    }
}
