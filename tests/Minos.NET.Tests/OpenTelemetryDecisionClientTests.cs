using System.Diagnostics;
using Minos.Telemetry;
using ZeroAlloc.Results;

namespace Minos.Tests;

[Collection(TelemetryListeners.Name)]
public sealed class OpenTelemetryDecisionClientTests
{
    private static readonly DecisionRequest Request = new(QuestionSets.TriageDefinition(), "s");

    private static readonly DecisionClientMetadata Metadata = new("typesafe", new Uri("https://api.test:8443/"), "minos-default");

    private sealed class Scripted(params Result<DecisionResponse, DecisionError>[] results) : IDecisionClient
    {
        private int _next;

        public int Calls => _next;

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(results[Math.Min(_next++, results.Length - 1)]);

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(DecisionClientMetadata) ? Metadata : null;

        public void Dispose()
        {
        }
    }

    private sealed class Pending(TaskCompletionSource<Result<DecisionResponse, DecisionError>> source) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(source.Task);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class Bare(Result<DecisionResponse, DecisionError> result) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(result);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task One_span_with_request_and_response_tags()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(new Scripted(OkWithEnvelope()));

        await stage.EvaluateAsync(Request);

        var span = capture.Span();
        Assert.Equal("evaluate minos-default", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        var start = capture.StartTags();
        Assert.Equal("evaluate", start.Tag("gen_ai.operation.name"));
        Assert.Equal("evaluate-set", start.Tag("minos.operation"));
        Assert.Equal("minos-default", start.Tag("gen_ai.request.model"));
        Assert.Equal(3, start.Tag("minos.request.question_count"));
        Assert.Equal("typesafe", start.Tag("gen_ai.provider.name"));
        Assert.Equal("api.test", start.Tag("server.address"));
        Assert.Equal(8443, start.Tag("server.port"));
        Assert.Equal("minos-1", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal(10, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(3, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal("r-1", span.GetTagItem("gen_ai.response.id"));
        Assert.Equal(0.002, span.GetTagItem("minos.usage.cost"));
    }

    [Fact]
    public async Task The_request_model_names_the_span_over_the_default()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(new Scripted(OkWithEnvelope()));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), "s") { Model = "minos-asked" });

        Assert.Equal("evaluate minos-asked", capture.Span().DisplayName);
        Assert.Equal("minos-asked", capture.StartTags().Tag("gen_ai.request.model"));
    }

    [Fact]
    public async Task Failure_sets_error_type_and_status()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(new Scripted(Fail(DecisionErrorKind.Server)));

        var result = await stage.EvaluateAsync(Request);

        Assert.True(result.IsFailure);
        var span = capture.Span();
        var expected = DecisionTelemetry.ErrorTypeOf(DecisionErrorKind.Server);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(expected, span.GetTagItem("error.type"));
        Assert.Null(span.GetTagItem("gen_ai.response.model"));
        var point = capture.OnlyPoint("gen_ai.client.operation.duration");
        Assert.Equal(expected, point.Tag("error.type"));
        Assert.Empty(capture.Points("minos.answer.confidence"));
    }

    [Fact]
    public async Task Metrics_record_duration_tokens_and_each_confidence()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(new Scripted(OkWithEnvelope()));

        await stage.EvaluateAsync(Request);

        var duration = capture.OnlyPoint("gen_ai.client.operation.duration");
        Assert.Equal("s", duration.Unit);
        Assert.Equal("minos-1", duration.Tag("gen_ai.response.model"));
        Assert.Equal("api.test", duration.Tag("server.address"));
        Assert.Equal(10, capture.OnlyPoint("gen_ai.client.inference.operation.input_tokens").Value);
        Assert.Equal(3, capture.OnlyPoint("gen_ai.client.inference.operation.output_tokens").Value);
        Assert.Equal(10, capture.OnlyPoint("gen_ai.client.inference.usage.input_tokens").Value);
        Assert.Equal(3, capture.OnlyPoint("gen_ai.client.inference.usage.output_tokens").Value);
        var confidences = capture.Points("minos.answer.confidence");
        Assert.Equal([0.8, 0.6], confidences.Select(point => point.Value));
        Assert.All(confidences, point => Assert.Equal("evaluate-set", point.Tag("minos.operation")));
    }

    [Fact]
    public async Task Missing_metadata_drops_server_tags_and_names_the_provider_unknown()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(new Bare(OkWithEnvelope()));

        await stage.EvaluateAsync(Request);

        var start = capture.StartTags();
        Assert.Equal("unknown", start.Tag("gen_ai.provider.name"));
        Assert.Equal("unknown", start.Tag("gen_ai.request.model"));
        Assert.Null(start.Tag("server.address"));
        Assert.Null(start.Tag("server.port"));
        var duration = capture.OnlyPoint("gen_ai.client.operation.duration");
        Assert.Null(duration.Tag("server.address"));
        Assert.Null(duration.Tag("server.port"));
    }

    [Fact]
    public async Task Missing_metadata_still_uses_the_request_model()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(new Bare(OkWithEnvelope()));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), "s") { Model = "minos-asked" });

        Assert.Equal("minos-asked", capture.StartTags().Tag("gen_ai.request.model"));
    }

    [Fact]
    public async Task Returns_the_inner_call_when_nothing_listens()
    {
        var pending = new TaskCompletionSource<Result<DecisionResponse, DecisionError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stage = new OpenTelemetryDecisionClient(new Pending(pending));

        var call = stage.EvaluateAsync(Request);

        Assert.False(call.IsCompleted);
        Assert.Same(pending.Task, call.AsTask());
        pending.SetResult(OkWithEnvelope());
        Assert.True((await pending.Task).IsSuccess);
        Assert.True(call.IsCompleted);
    }
    [Fact]
    public async Task One_span_covers_every_retry()
    {
        using var capture = new TelemetryCapture();
        var inner = new Scripted(Fail(DecisionErrorKind.Server), OkWithEnvelope());
        using var client = inner.AsBuilder().UseOpenTelemetry().UseRetries(o => o.InitialBackoff = TimeSpan.FromMilliseconds(1)).Build();

        var result = await client.EvaluateAsync(Request);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        var span = capture.Span();
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.Equal("minos-1", span.GetTagItem("gen_ai.response.model"));
    }

    [Fact]
    public async Task Rejects_a_null_request_and_a_null_builder()
    {
        using var stage = new OpenTelemetryDecisionClient(new Scripted(OkWithEnvelope()));

        await Assert.ThrowsAsync<ArgumentNullException>("request", async () => await stage.EvaluateAsync(null!));
        Assert.Throws<ArgumentNullException>("builder", () => OpenTelemetryDecisionClientBuilderExtensions.UseOpenTelemetry(null!));
        Assert.Throws<ArgumentNullException>("innerClient", () => new OpenTelemetryDecisionClient(null!));
    }

    private static Result<DecisionResponse, DecisionError> Fail(DecisionErrorKind kind)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, "x"));

    private static Result<DecisionResponse, DecisionError> OkWithEnvelope()
    {
        var built = new DecisionResponse(
            QuestionSets.TriageDefinition(),
            [QuestionAnswer.Noul(0.9), QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]), QuestionAnswer.Score(2, 1.7, 0.6, [0.1, 0.1, 0.8])]);
        return Result<DecisionResponse, DecisionError>.Success(
            new DecisionResponse(built.Definition, built.Slots, built.Probabilities, "minos-1", 10, 3, 0.002, "r-1", "typesafe"));
    }
}
