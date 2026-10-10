using System.Diagnostics;
using ZeroAlloc.Results;
using ZeroAlloc.Telemetry;
using static Minos.Telemetry.DecisionTelemetry;

namespace Minos.Telemetry;

/// <summary>
/// The client's two raw System One operations, instrumented: ZeroAlloc.Telemetry generates
/// <c>DecisionOperationsInstrumented</c>, which opens one CLIENT span per call and records the GenAI and <c>minos.*</c>
/// metrics. With nothing listening the proxy returns the inner call's task itself. Span and metric tags never carry
/// request or answer content.
/// </summary>
/// <remarks>
/// The bucket literals repeat <see cref="DecisionTelemetry.DurationBuckets"/>, <see cref="DecisionTelemetry.TokenBuckets"/> and
/// <see cref="DecisionTelemetry.ConfidenceBuckets"/>, since an attribute argument cannot read a property; a test checks the
/// instruments' advice against them.
/// </remarks>
[Instrument(SourceName)]
internal interface IDecisionOperations
{
    /// <summary>Evaluates an untyped request.</summary>
    [Trace("evaluate {request.Model}", Kind = ActivityKind.Client, ErrorWhen = "IsFailure", TagsAtStart = true, ExceptionDescription = false)]
    [TraceTagConstant(OperationName, EvaluateOperation)]
    [TraceTagConstant(DecisionOperation, DecisionLog.Evaluate)]
    [TraceTagFromResult(ResponseModel, "Value.Model", When = "IsSuccess")]
    [TraceTagFromResult(InputTokens, "Value.Usage.InputTokens", When = "IsSuccess")]
    [TraceTagFromResult(OutputTokens, "Value.Usage.OutputTokens", When = "IsSuccess")]
    [TraceTagFromResult(ResponseId, "Value.Id", When = "IsSuccess")]
    [TraceTagFromResult(Cost, "Value.Usage.Cost", When = "IsSuccess")]
    [TraceTagFromResult(ErrorType, "Error.ErrorType", When = "IsFailure")]
    [Histogram(OperationDuration, Unit = Seconds, Buckets = new[] { 0.01, 0.02, 0.04, 0.08, 0.16, 0.32, 0.64, 1.28, 2.56, 5.12, 10.24, 20.48, 40.96, 81.92 })]
    [HistogramFromResult(InputTokenHistogram, "Value.Usage.InputTokens", When = "IsSuccess", Unit = Tokens, Buckets = new[] { 1d, 4, 16, 64, 256, 1024, 4096, 16384, 65536, 262144, 1048576, 4194304, 16777216, 67108864 })]
    [HistogramFromResult(OutputTokenHistogram, "Value.Usage.OutputTokens", When = "IsSuccess", Unit = Tokens, Buckets = new[] { 1d, 4, 16, 64, 256, 1024, 4096, 16384, 65536, 262144, 1048576, 4194304, 16777216, 67108864 })]
    [CountFromResult(InputTokenCounter, "Value.Usage.InputTokens", When = "IsSuccess", Unit = Tokens)]
    [CountFromResult(OutputTokenCounter, "Value.Usage.OutputTokens", When = "IsSuccess", Unit = Tokens)]
    [HistogramFromResult(AnswerConfidence, "Value.Confidences", When = "IsSuccess", Each = true, Unit = Ratio, Buckets = new[] { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.99 })]
    [MetricTagConstant(OperationName, EvaluateOperation)]
    [MetricTagConstant(TokenModality, Text, Metric = InputTokenCounter)]
    [MetricTagConstant(TokenModality, Text, Metric = OutputTokenCounter)]
    [MetricTagConstant(DecisionOperation, DecisionLog.Evaluate, Metric = AnswerConfidence)]
    [MetricTagFromResult(ErrorType, "Error.ErrorType", When = "IsFailure", Metric = OperationDuration)]
    [MetricTagFromResult(ResponseModel, "Value.Model", When = "IsSuccess", Metric = OperationDuration)]
    [MetricTagFromResult(ResponseModel, "Value.Model", When = "IsSuccess", Metric = InputTokenHistogram)]
    [MetricTagFromResult(ResponseModel, "Value.Model", When = "IsSuccess", Metric = OutputTokenHistogram)]
    ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
        [TraceTag(RequestModel, "Model")]
        [TraceTag(QuestionCount, "Questions.Count")]
        [MetricTag(RequestModel, "Model")]
        SystemOneRequest request,
        [TraceTag(ProviderName)]
        [MetricTag(ProviderName)]
        string provider,
        [TraceTag(ServerAddress, "Host")]
        [TraceTag(ServerPort, "Port")]
        [MetricTag(ServerAddress, "Host", Metric = OperationDuration)]
        [MetricTag(ServerPort, "Port", Metric = OperationDuration)]
        Uri endpoint,
        CancellationToken ct);

    /// <summary>Lists the models; never called for OpenRouter, whose failure the client returns before any call.</summary>
    [Trace(ListModelsOperation, Kind = ActivityKind.Client, ErrorWhen = "IsFailure", TagsAtStart = true, ExceptionDescription = false)]
    [TraceTagConstant(OperationName, ListModelsOperation)]
    [TraceTagConstant(DecisionOperation, DecisionLog.ListModels)]
    [TraceTagFromResult(ErrorType, "Error.ErrorType", When = "IsFailure")]
    [Histogram(OperationDuration, Unit = Seconds, Buckets = new[] { 0.01, 0.02, 0.04, 0.08, 0.16, 0.32, 0.64, 1.28, 2.56, 5.12, 10.24, 20.48, 40.96, 81.92 })]
    [MetricTagConstant(OperationName, ListModelsOperation)]
    [MetricTagFromResult(ErrorType, "Error.ErrorType", When = "IsFailure")]
    ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(
        [TraceTag(ProviderName)]
        [MetricTag(ProviderName)]
        string provider,
        [TraceTag(ServerAddress, "Host")]
        [TraceTag(ServerPort, "Port")]
        [MetricTag(ServerAddress, "Host")]
        [MetricTag(ServerPort, "Port")]
        Uri endpoint,
        CancellationToken ct);
}
