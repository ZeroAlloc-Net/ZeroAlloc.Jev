using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Minos.Integration.Tests;

/// <summary>Builds the <c>AddDecisionClient</c> provider the dependency injection integration tests share.</summary>
internal static class DependencyInjectionHarness
{
    // One retry, a short backoff and no jitter, through IntegrationClient.Configure as the hand-built clients are;
    // timeout null keeps the library's default.
    public static ServiceProvider Provider(WireMockFixture fixture, AttemptCount attempts, TimeSpan? timeout)
    {
        var services = new ServiceCollection();
        services
            .AddDecisionClient(options =>
            {
                IntegrationClient.Configure(options, maxRetries: 1);
                options.BaseAddress = fixture.BaseAddress;
                if (timeout is { } perAttempt)
                {
                    options.Timeout = perAttempt;
                }
            })
            .HttpClient.AddHttpMessageHandler(() => new CountingHandler(attempts));
        return services.BuildServiceProvider();
    }

    // The same fixture settings as Provider, but every one bound from an in-memory configuration section, as an
    // appsettings.json would supply them. timeout null leaves the key out, so the library's default applies.
    public static ServiceProvider BoundProvider(WireMockFixture fixture, AttemptCount attempts, int maxRetries, TimeSpan? timeout)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Minos:ApiKey"] = "integration-key",
            ["Minos:BaseAddress"] = fixture.BaseAddress.AbsoluteUri,
            ["Minos:MaxRetries"] = maxRetries.ToString(CultureInfo.InvariantCulture),
            ["Minos:InitialBackoff"] = "00:00:00.010",
            ["Minos:MaxRetryDelay"] = "00:00:05",
            ["Minos:Jitter"] = "false",
        };
        if (timeout is { } perAttempt)
        {
            settings["Minos:Timeout"] = perAttempt.ToString("c", CultureInfo.InvariantCulture);
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services
            .AddDecisionClient(configuration.GetSection("Minos"))
            .HttpClient.AddHttpMessageHandler(() => new CountingHandler(attempts));
        return services.BuildServiceProvider();
    }

    /// <summary>How many attempts have started.</summary>
    internal sealed class AttemptCount
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);
    }

    /// <summary>Counts each attempt as it starts, then passes it on.</summary>
    private sealed class CountingHandler(AttemptCount attempts) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            attempts.Increment();
            return base.SendAsync(request, cancellationToken);
        }
    }
}
