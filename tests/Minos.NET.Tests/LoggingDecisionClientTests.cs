using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Results;

namespace Minos.Tests;

public sealed class LoggingDecisionClientTests
{
    private static readonly DecisionRequest Request = new(QuestionSets.TriageDefinition(), "s");

    private sealed class Scripted(Result<DecisionResponse, DecisionError> result, DecisionClientMetadata? metadata = null) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(result);

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(DecisionClientMetadata) ? metadata : null;

        public void Dispose()
        {
        }
    }

    private sealed class Throwing(Exception exception) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromException<Result<DecisionResponse, DecisionError>>(exception);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingSynchronously(Exception exception) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => throw exception;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class Deferred : IDecisionClient
    {
        private readonly TaskCompletionSource<Result<DecisionResponse, DecisionError>> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(Result<DecisionResponse, DecisionError> result) => _source.SetResult(result);

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(_source.Task);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static DecisionClientMetadata Metadata() => new("typesafe", new Uri("https://api.test/"), "minos-default");

    private static ILogger Logger(LogCapture logs) => logs.Factory.CreateLogger(DecisionLog.Category);

    private static Result<DecisionResponse, DecisionError> Ok()
        => Result<DecisionResponse, DecisionError>.Success(
            new DecisionResponse(Request.Definition, [QuestionAnswer.Noul(0.5), QuestionAnswer.Choice(0, 0.8, [0.8, 0.1, 0.1]), QuestionAnswer.Score(1, 0.5, 0.7, [0.1, 0.7, 0.2])]));

    private static Result<DecisionResponse, DecisionError> Fail(DecisionErrorKind kind, string message, int? status = null)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, message) { StatusCode = status });

    [Fact]
    public async Task Logs_success_once_with_the_neutral_operation()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        using var stage = new LoggingDecisionClient(new Scripted(Ok(), Metadata()), Logger(logs));

        await stage.EvaluateAsync(Request);

        var record = logs.Only(1001);
        Assert.Equal([1001], logs.EventIds);
        Assert.Equal("EvaluationSucceeded", record.Id.Name);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.Equal("evaluate-set", LogAssert.Field(record, "Operation"));
        Assert.Equal("minos-default", LogAssert.Field(record, "Model"));
        Assert.Equal("typesafe", LogAssert.Field(record, "Provider"));
        Assert.Equal("3", LogAssert.Field(record, "QuestionCount"));
        Assert.Equal(DecisionLog.Category, record.Category);
        Assert.True(double.Parse(LogAssert.Field(record, "DurationMs")!, System.Globalization.CultureInfo.InvariantCulture) >= 0);
    }

    [Fact]
    public async Task The_request_model_wins_over_the_default()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        using var stage = new LoggingDecisionClient(new Scripted(Ok(), Metadata()), Logger(logs));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), "s") { Model = "minos-x" });

        Assert.Equal("minos-x", LogAssert.Field(logs.Only(1001), "Model"));
    }

    [Fact]
    public async Task Missing_metadata_logs_unknown_provider_and_the_request_model()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        using var stage = new LoggingDecisionClient(new Scripted(Ok()), Logger(logs));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), "s") { Model = "minos-x" });
        await stage.EvaluateAsync(Request);

        var records = logs.Records;
        Assert.Equal(2, records.Count);
        Assert.Equal("unknown", LogAssert.Field(records[0], "Provider"));
        Assert.Equal("minos-x", LogAssert.Field(records[0], "Model"));
        Assert.Equal("unknown", LogAssert.Field(records[1], "Provider"));
        Assert.Equal("unknown", LogAssert.Field(records[1], "Model"));
    }

    [Fact]
    public async Task Logs_failure_with_the_library_message()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        using var stage = new LoggingDecisionClient(
            new Scripted(Fail(DecisionErrorKind.Validation, "The API returned HTTP 422.", 422), Metadata()), Logger(logs));

        var result = await stage.EvaluateAsync(Request);

        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        var record = logs.Only(1002);
        Assert.Equal([1002], logs.EventIds);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("evaluate-set", LogAssert.Field(record, "Operation"));
        Assert.Equal("minos-default", LogAssert.Field(record, "Model"));
        Assert.Equal("Validation", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("422", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("The API returned HTTP 422.", LogAssert.Field(record, "ErrorMessage"));
    }

    [Theory]
    [InlineData(DecisionErrorKind.InvalidResponse, "quotes the body", "The response could not be read.")]
    [InlineData(DecisionErrorKind.Network, "https://secret.test/?key=1", "The request could not be sent.")]
    public async Task Logs_failure_with_the_safe_message(DecisionErrorKind kind, string message, string logged)
    {
        using var logs = new LogCapture(LogLevel.Debug);
        using var stage = new LoggingDecisionClient(new Scripted(Fail(kind, message, 200)), Logger(logs));

        await stage.EvaluateAsync(Request);

        Assert.Equal(logged, LogAssert.Field(logs.Only(1002), "ErrorMessage"));
    }

    [Fact]
    public async Task Logs_an_unexpected_exception_and_rethrows_it()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        var boom = new InvalidOperationException("boom");
        using var stage = new LoggingDecisionClient(new Throwing(boom), Logger(logs));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await stage.EvaluateAsync(Request));

        Assert.Same(boom, thrown);
        var record = logs.Only(1006);
        Assert.Equal([1006], logs.EventIds);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal("evaluate-set", LogAssert.Field(record, "Operation"));
        Assert.Same(boom, record.Exception);
    }

    [Fact]
    public async Task Logs_a_synchronous_throw_and_rethrows_it()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        using var stage = new LoggingDecisionClient(new ThrowingSynchronously(new InvalidOperationException("boom")), Logger(logs));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await stage.EvaluateAsync(Request));

        Assert.Equal([1006], logs.EventIds);
    }

    [Fact]
    public async Task Caller_cancellation_logs_nothing()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stage = new LoggingDecisionClient(new Throwing(new OperationCanceledException(cancellation.Token)), Logger(logs));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stage.EvaluateAsync(Request, cancellation.Token));

        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task Logs_the_outcome_of_a_call_that_completes_later()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        var inner = new Deferred();
        using var stage = new LoggingDecisionClient(inner, Logger(logs));

        var call = stage.EvaluateAsync(Request);
        Assert.False(call.IsCompleted);
        Assert.Empty(logs.Records);
        inner.Complete(Ok());
        await call;

        Assert.Equal([1001], logs.EventIds);
    }

    [Fact]
    public async Task Debug_disabled_logs_failures_but_not_successes()
    {
        using var logs = new LogCapture(LogLevel.Warning);
        using var ok = new LoggingDecisionClient(new Scripted(Ok()), Logger(logs));
        using var failing = new LoggingDecisionClient(new Scripted(Fail(DecisionErrorKind.Validation, "x", 422)), Logger(logs));

        await ok.EvaluateAsync(Request);
        await failing.EvaluateAsync(Request);

        Assert.Equal([1002], logs.EventIds);
    }

    [Fact]
    public async Task Returns_the_inner_call_when_every_level_is_off()
    {
        using var logs = new LogCapture(LogLevel.None);
        using var stage = new LoggingDecisionClient(new Scripted(Ok()), Logger(logs));

        var call = stage.EvaluateAsync(Request);

        Assert.True(call.IsCompletedSuccessfully);
        await call;
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task Rejects_a_null_logger_and_a_null_request()
    {
        using var logs = new LogCapture();
        var inner = new Scripted(Ok());
        using var stage = new LoggingDecisionClient(inner, Logger(logs));

        Assert.Throws<ArgumentNullException>(() => new LoggingDecisionClient(inner, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await stage.EvaluateAsync(null!));
    }

    [Fact]
    public void Use_logging_without_a_factory_adds_no_stage()
    {
        var inner = new Scripted(Ok());

        Assert.Same(inner, inner.AsBuilder().UseLogging().Build());
    }

    [Fact]
    public void Use_logging_reads_the_factory_from_the_services()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        Assert.IsType<LoggingDecisionClient>(new Scripted(Ok()).AsBuilder().UseLogging().Build(services));
    }

    [Fact]
    public async Task Use_logging_prefers_the_factory_it_is_given()
    {
        using var logs = new LogCapture(LogLevel.Debug);
        using var client = new Scripted(Ok(), Metadata()).AsBuilder().UseLogging(logs.Factory).Build();

        await client.EvaluateAsync(Request);

        Assert.Equal([1001], logs.EventIds);
    }
}
