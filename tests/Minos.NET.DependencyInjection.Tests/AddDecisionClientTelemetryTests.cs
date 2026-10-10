using Microsoft.Extensions.DependencyInjection;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary>A resolved client's telemetry, which is always on, with no setup beyond a listener on the <c>Minos</c> source.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class AddDecisionClientTelemetryTests
{
    [Fact]
    public async Task ResolvedClient_EmitsAMinosSpan_WithNoTelemetrySetup()
    {
        using var capture = new TelemetryCapture();
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddDecisionClient(Options("http://default.local/")).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal("evaluate", capture.StartTags().Tag("minos.operation"));
        Assert.Equal("Minos", capture.Span().Source.Name);
    }
}
