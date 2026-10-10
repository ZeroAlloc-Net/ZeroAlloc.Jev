using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

/// <summary>
/// A client from <c>AddDecisionClient</c> over WireMock, sending through the factory's <see cref="HttpClient"/> and its default
/// primary handler. Attempts are counted on the client, by a handler added through the returned builder, so no test
/// here reads WireMock's log. The held-response time-out test lives in <see cref="DependencyInjectionTimeoutTests"/>,
/// because <see cref="WireMockFixture"/> forbids sharing a class with a test that leaves a response pending.
/// </summary>
public sealed class DependencyInjectionTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public DependencyInjectionTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task RetriedServiceUnavailable_Succeeds()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("di-retry")
            .WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("di-retry")
            .WhenStateIs("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));
        var attempts = new DependencyInjectionHarness.AttemptCount();
        using var provider = DependencyInjectionHarness.Provider(_fixture, attempts, timeout: null);

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, Assert.IsType<NoulAnswer>(result.Value.Answers["is_urgent"]).Noul, 3);
        Assert.Equal(2, attempts.Value);
    }

    [Fact]
    public async Task BoundMaxRetries_IsTheNumberOfRetriesOnTheWire()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));
        var attempts = new DependencyInjectionHarness.AttemptCount();
        using var provider = DependencyInjectionHarness.BoundProvider(_fixture, attempts, maxRetries: 3, timeout: null);

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(503, result.Error.StatusCode);
        Assert.Equal(4, attempts.Value);
    }
}
