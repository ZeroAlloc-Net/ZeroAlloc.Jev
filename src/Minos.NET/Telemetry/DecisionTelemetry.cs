namespace Minos.Telemetry;

/// <summary>
/// The span, metric and attribute names <c>IDecisionOperations</c> uses: OpenTelemetry GenAI names where they fit,
/// <c>minos.*</c> for the rest. The GenAI token metrics follow the <c>semantic-conventions-genai</c> repository's main
/// branch, which has no release yet; Milestone 5 rechecks every name before 1.0.
/// </summary>
internal static class DecisionTelemetry
{
    /// <summary>The ActivitySource and Meter name.</summary>
    public const string SourceName = "Minos";

    /// <summary>The GenAI operation: <see cref="EvaluateOperation"/> or <see cref="ListModelsOperation"/>.</summary>
    public const string OperationName = "gen_ai.operation.name";

    /// <summary>The GenAI provider: <see cref="TypeSafe"/> or <see cref="OpenRouter"/>.</summary>
    public const string ProviderName = "gen_ai.provider.name";

    /// <summary>The model the request asked for.</summary>
    public const string RequestModel = "gen_ai.request.model";

    /// <summary>The model that answered.</summary>
    public const string ResponseModel = "gen_ai.response.model";

    /// <summary>OpenRouter's generation id.</summary>
    public const string ResponseId = "gen_ai.response.id";

    /// <summary>The input tokens, on the span.</summary>
    public const string InputTokens = "gen_ai.usage.input_tokens";

    /// <summary>The output tokens, on the span.</summary>
    public const string OutputTokens = "gen_ai.usage.output_tokens";

    /// <summary>The token modality, on the usage counters.</summary>
    public const string TokenModality = "gen_ai.token.modality";

    /// <summary>The base address's host.</summary>
    public const string ServerAddress = "server.address";

    /// <summary>The base address's port.</summary>
    public const string ServerPort = "server.port";

    /// <summary>The <see cref="DecisionErrorKind"/> name of a failure.</summary>
    public const string ErrorType = "error.type";

    /// <summary>The client's operation, the logging operation names: <c>evaluate</c>, <c>evaluate-set</c> or <c>list-models</c>.</summary>
    public const string DecisionOperation = "minos.operation";

    /// <summary>The number of questions asked.</summary>
    public const string QuestionCount = "minos.request.question_count";

    /// <summary>The cost in US dollars OpenRouter reports.</summary>
    public const string Cost = "minos.usage.cost";

    /// <summary>The custom GenAI operation value for an evaluation.</summary>
    public const string EvaluateOperation = "evaluate";

    /// <summary>The custom GenAI operation value, and the span name, for model listing.</summary>
    public const string ListModelsOperation = "list_models";

    /// <summary>The custom GenAI provider value for TypeSafe's API.</summary>
    public const string TypeSafe = "typesafe";

    /// <summary>The custom GenAI provider value for OpenRouter.</summary>
    public const string OpenRouter = "openrouter";

    /// <summary>The token modality Jev always uses.</summary>
    public const string Text = "text";

    /// <summary>OpenTelemetry's <c>error.type</c> fallback for a value with no name.</summary>
    public const string Other = "_OTHER";

    /// <summary>The operation's duration.</summary>
    public const string OperationDuration = "gen_ai.client.operation.duration";

    /// <summary>The input tokens per operation.</summary>
    public const string InputTokenHistogram = "gen_ai.client.inference.operation.input_tokens";

    /// <summary>The output tokens per operation.</summary>
    public const string OutputTokenHistogram = "gen_ai.client.inference.operation.output_tokens";

    /// <summary>The input tokens used, as a running total.</summary>
    public const string InputTokenCounter = "gen_ai.client.inference.usage.input_tokens";

    /// <summary>The output tokens used, as a running total.</summary>
    public const string OutputTokenCounter = "gen_ai.client.inference.usage.output_tokens";

    /// <summary>Each Choice or Score answer's confidence.</summary>
    public const string AnswerConfidence = "minos.answer.confidence";

    /// <summary>Seconds.</summary>
    public const string Seconds = "s";

    /// <summary>Tokens.</summary>
    public const string Tokens = "{token}";

    /// <summary>A dimensionless ratio.</summary>
    public const string Ratio = "1";

    /// <summary>
    /// Gets the GenAI duration buckets. <c>IDecisionOperations</c>' attributes repeat them as literals, since an attribute
    /// argument cannot read a property, and tests check each attribute and each instrument's advice against these.
    /// </summary>
    public static ReadOnlySpan<double> DurationBuckets => [0.01, 0.02, 0.04, 0.08, 0.16, 0.32, 0.64, 1.28, 2.56, 5.12, 10.24, 20.48, 40.96, 81.92];

    /// <summary>Gets the GenAI token buckets; the attributes repeat them, as for <see cref="DurationBuckets"/>.</summary>
    public static ReadOnlySpan<double> TokenBuckets => [1, 4, 16, 64, 256, 1024, 4096, 16384, 65536, 262144, 1048576, 4194304, 16777216, 67108864];

    /// <summary>Gets the confidence buckets; the attributes repeat them, as for <see cref="DurationBuckets"/>.</summary>
    public static ReadOnlySpan<double> ConfidenceBuckets => [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.99];

    /// <summary>Gets the GenAI provider value for <paramref name="provider"/>.</summary>
    public static string ProviderOf(DecisionProvider provider) => provider == DecisionProvider.OpenRouter ? OpenRouter : TypeSafe;

    /// <summary>Gets the <c>error.type</c> value for <paramref name="kind"/>: its name, as a literal, so reading it allocates nothing.</summary>
    public static string ErrorTypeOf(DecisionErrorKind kind) => kind switch
    {
        DecisionErrorKind.Unauthorized => nameof(DecisionErrorKind.Unauthorized),
        DecisionErrorKind.Validation => nameof(DecisionErrorKind.Validation),
        DecisionErrorKind.RateLimited => nameof(DecisionErrorKind.RateLimited),
        DecisionErrorKind.Overloaded => nameof(DecisionErrorKind.Overloaded),
        DecisionErrorKind.Server => nameof(DecisionErrorKind.Server),
        DecisionErrorKind.Http => nameof(DecisionErrorKind.Http),
        DecisionErrorKind.Network => nameof(DecisionErrorKind.Network),
        DecisionErrorKind.Timeout => nameof(DecisionErrorKind.Timeout),
        DecisionErrorKind.InvalidResponse => nameof(DecisionErrorKind.InvalidResponse),
        DecisionErrorKind.Unsupported => nameof(DecisionErrorKind.Unsupported),
        DecisionErrorKind.InvalidQuestions => nameof(DecisionErrorKind.InvalidQuestions),
        DecisionErrorKind.Disposed => nameof(DecisionErrorKind.Disposed),
        _ => Other,
    };
}
