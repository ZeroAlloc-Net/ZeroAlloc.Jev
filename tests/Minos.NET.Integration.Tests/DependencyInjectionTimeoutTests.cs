using Microsoft.Extensions.DependencyInjection;

namespace Minos.Integration.Tests;

/// <summary>
/// A client from <c>AddDecisionClient</c> whose response the server holds. It has its own <see cref="WireMockFixture"/> and
/// shares its class with no test that reads the log or uses scenario state, because the held request stays pending
/// after the test ends.
/// </summary>
public sealed class DependencyInjectionTimeoutTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public DependencyInjectionTimeoutTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task SlowResponse_TimesOutPerAttempt_WithTheOptionsTimeout()
    {
        // The server holds every response until the test has its result, so an attempt can only end through the
        // options' per-attempt time-out, which the factory's HttpClient got from DecisionClient.ConfigureHttpClient.
        using var held = _fixture.HoldEveryResponse();
        var attempts = new DependencyInjectionHarness.AttemptCount();
        using var provider = DependencyInjectionHarness.Provider(_fixture, attempts, timeout: TimeSpan.FromMilliseconds(300));

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Timeout, result.Error.Kind);
        Assert.Equal(2, attempts.Value);
    }

    [Fact]
    public async Task BoundTimeout_TimesOutEachAttempt()
    {
        // As above, but the 300 ms per-attempt time-out and the single attempt both come from configuration.
        using var held = _fixture.HoldEveryResponse();
        var attempts = new DependencyInjectionHarness.AttemptCount();
        using var provider = DependencyInjectionHarness.BoundProvider(
            _fixture, attempts, maxRetries: 0, timeout: TimeSpan.FromMilliseconds(300));

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Timeout, result.Error.Kind);
        Assert.Equal(1, attempts.Value);
    }
}
