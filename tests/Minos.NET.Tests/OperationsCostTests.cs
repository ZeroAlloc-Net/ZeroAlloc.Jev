using System.Text.Json;
using Minos.Serialization;
using Minos.Telemetry;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace Minos.Tests;

/// <summary>What the instrumented proxies cost with nothing listening.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class OperationsCostTests
{
    private static readonly Uri Endpoint = new("https://api.typesafe.ai/");

    [Fact]
    public void NothingListening_RawCall_AddsNothing()
    {
        var proxy = new DecisionOperationsInstrumented(Completed.Instance);
        var request = JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;

        AllocationGate.AssertBudgetValueTask(0, 1000, () => proxy.EvaluateAsync(request, "typesafe", Endpoint, CancellationToken.None), "ProxyRaw");
    }

    [Fact]
    public void NothingListening_ListModels_AddsNothing()
    {
        var proxy = new DecisionOperationsInstrumented(Completed.Instance);

        AllocationGate.AssertBudgetValueTask(0, 1000, () => proxy.ListModelsAsync("typesafe", Endpoint, CancellationToken.None), "ProxyListModels");
    }

    // Replaces the typed and built-set proxy gates: the telemetry stage now traces every neutral call, typed and built
    // sets included, and with nothing listening it returns the inner call itself.
    [Fact]
    public void NothingListening_TelemetryStage_OverASynchronousCall_AddsNothing()
    {
        using var stage = new OpenTelemetryDecisionClient(new CompletedClient());
        var request = new DecisionRequest(QuestionSets.UrgencyDefinition(), "s");

        AllocationGate.AssertBudgetValueTask(0, 1000, () => stage.EvaluateAsync(request, CancellationToken.None), "StageEvaluate");
    }

    /// <summary>Returns the same completed results every call, so a gate measures the proxy and nothing else.</summary>
    private sealed class Completed : IDecisionOperations
    {
        public static readonly Completed Instance = new();

        private static readonly SystemOneResponse Response =
            JsonSerializer.Deserialize(Fixture.Text("response-noul.json"), DecisionJsonContext.Default.SystemOneResponse)!;

        private static readonly ModelList Models = new() { Models = [] };

        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(SystemOneRequest request, string provider, Uri endpoint, CancellationToken ct)
            => new(Result<SystemOneResponse, DecisionError>.Success(Response));

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string provider, Uri endpoint, CancellationToken ct)
            => new(Result<ModelList, DecisionError>.Success(Models));
    }

    /// <summary>Answers every call synchronously with the same prebuilt response.</summary>
    private sealed class CompletedClient : IDecisionClient
    {
        private static readonly Result<DecisionResponse, DecisionError> Answer
            = Result<DecisionResponse, DecisionError>.Success(ClientTestKit.CapturingClient.Canned(QuestionSets.UrgencyDefinition()));

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Answer);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
