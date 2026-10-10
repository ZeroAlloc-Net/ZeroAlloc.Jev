using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Minos.Docs.Tests;

// Serialized with the other test class that sets the key environment variables.
[Collection("key environment")]
public sealed class DependencyInjectionTests
{
    private const string UrgentResponse = """
        {
          "model": "jev-1.13.0",
          "answers": { "is_urgent": { "type": "noul", "noul": 0.93 } },
          "usage": { "input_tokens": 41, "output_tokens": 3 }
        }
        """;

    private static readonly Dictionary<string, string?> Keys = new(StringComparer.Ordinal)
    {
        ["TypeSafe:ApiKey"] = "typesafe-key",
        ["OpenRouter:ApiKey"] = "openrouter-key",
    };

    [Fact]
    public async Task ARegisteredClient_IsResolvedAndEvaluates()
    {
        var builder = NewBuilder();
        using var handler = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        DecisionRegistration.AddDecision(builder).HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        using var host = builder.Build();
        await host.StartAsync();

        var triage = host.Services.GetRequiredService<InboxTriage>();
        var outcome = await triage.TriageAsync("Help! My payouts have been failing for 3 days.", CancellationToken.None);

        Assert.Equal("urgent", outcome);
        Assert.Collection(
            handler.Requests,
            sent =>
            {
                Assert.Equal(new Uri("https://api.typesafe.ai/v1/systemone"), sent.Uri);
                Assert.Equal("Bearer typesafe-key", sent.Authorization);
            });
        Assert.Same(host.Services.GetRequiredService<IDecisionClient>(), host.Services.GetRequiredService<IDecisionClient>());
    }

    [Fact]
    public async Task TheContainer_DisposesTheClientWithTheProvider()
    {
        var builder = NewBuilder();
        DecisionRegistration.AddDecision(builder);
        var host = builder.Build();
        var client = host.Services.GetRequiredService<IDecisionClient>();

        host.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await client.EvaluateAsync<InboxCheck>("Help!", CancellationToken.None));
    }

    [Fact]
    public async Task KeyedClients_EachGoToTheirOwnProvider()
    {
        var builder = NewBuilder();
        using var typeSafe = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        using var openRouter = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        KeyedRegistration.AddKeyedClients(builder);
        builder.Services.AddHttpClient("Minos.NET:typesafe").ConfigurePrimaryHttpMessageHandler(() => typeSafe);
        builder.Services.AddHttpClient("Minos.NET:openrouter").ConfigurePrimaryHttpMessageHandler(() => openRouter);
        using var host = builder.Build();
        await host.StartAsync();

        var outcome = await host.Services.GetRequiredService<InboxRouter>().TriageAsync("Help!", CancellationToken.None);
        var direct = await host.Services.GetRequiredKeyedService<IDecisionClient>("typesafe")
            .EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);

        Assert.Equal("urgent, via OpenRouter", outcome);
        Assert.True(direct.IsSuccess);
        Assert.Collection(
            openRouter.Requests,
            sent =>
            {
                Assert.Equal(new Uri("https://openrouter.ai/api/v1/systemone"), sent.Uri);
                Assert.Equal("Bearer openrouter-key", sent.Authorization);
            });
        Assert.Collection(
            typeSafe.Requests,
            sent =>
            {
                Assert.Equal(new Uri("https://api.typesafe.ai/v1/systemone"), sent.Uri);
                Assert.Equal("Bearer typesafe-key", sent.Authorization);
            });
        Assert.NotSame(
            host.Services.GetRequiredKeyedService<IDecisionClient>("typesafe"),
            host.Services.GetRequiredKeyedService<IDecisionClient>("openrouter"));
        Assert.Null(host.Services.GetService<IDecisionClient>());
    }

    [Fact]
    public void WithNoOptions_TheKeyComesFromTheEnvironment()
    {
        using var environment = new KeyEnvironment("environment-key", openRouter: null);
        var services = new ServiceCollection();
        KeyedRegistration.AddFromEnvironment(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDecisionClient>());
        Assert.NotNull(provider.GetRequiredKeyedService<IDecisionClient>("backup"));
    }

    [Fact]
    public void TheAppsettingsOnThePage_BindToTheOptions()
    {
        var builder = NewBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Minos:ApiKey"] = "typesafe-key" });
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(PageBlock("json")));
        builder.Configuration.AddJsonStream(json);
        KeyedRegistration.AddFromConfiguration(builder);
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>();
        var typeSafe = options.Get(Microsoft.Extensions.Options.Options.DefaultName);
        var openRouter = options.Get("openrouter");

        Assert.Equal(DecisionProvider.TypeSafe, typeSafe.Provider);
        Assert.Equal("jev-latest", typeSafe.Model);
        Assert.Equal(TimeSpan.FromMinutes(1), typeSafe.Timeout);
        Assert.Equal(2, typeSafe.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), typeSafe.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(30), typeSafe.MaxRetryDelay);
        Assert.True(typeSafe.Jitter);
        Assert.Equal("typesafe-key", typeSafe.ApiKey);
        Assert.Equal(DecisionProvider.OpenRouter, openRouter.Provider);
        Assert.Equal(0, openRouter.MaxRetries);
        Assert.Equal("openrouter-key", openRouter.ApiKey);
    }

    [Fact]
    public void ADelegateRegisteredLater_OverridesBoundValues_AndARepeatCallRegistersNoSecondClient()
    {
        var builder = NewBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Minos:MaxRetries"] = "5" });
        builder.Services.AddDecisionClient(builder.Configuration.GetSection("Minos"));
        builder.Services.AddDecisionClient(options =>
        {
            options.ApiKey = "key";
            options.MaxRetries = 1;
        });
        builder.Services.AddDecisionClient(options => options.Model = "jev-preview");
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>().CurrentValue;

        Assert.Equal(1, options.MaxRetries);
        Assert.Equal("jev-preview", options.Model);
        Assert.Collection(
            builder.Services.Where(descriptor => descriptor.ServiceType == typeof(IDecisionClient)),
            descriptor => Assert.Null(descriptor.ServiceKey));
    }

    [Fact]
    public async Task AnInvalidValue_FailsTheHostAtStartup_WithTheCoresMessage()
    {
        var builder = NewBuilder();
        builder.Services.AddDecisionClient(options =>
        {
            options.ApiKey = "key";
            options.MaxRetries = 11;
        });
        using var host = builder.Build();

        var message = await KeyedRegistration.TryStartAsync(host);

        Assert.Equal("Minos is misconfigured: MaxRetries must be between 0 and 10. (Parameter 'options')", message);
    }

    [Fact]
    public async Task AMissingApiKey_FailsTheHostAtStartup()
    {
        using var environment = new KeyEnvironment(typeSafe: null, openRouter: null);
        var builder = NewBuilder();
        builder.Services.AddDecisionClient();
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Collection(exception.Failures, failure => Assert.Contains("TYPESAFE_API_KEY", failure, StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAHost_TheFailureComesWhenTheClientIsFirstResolved()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient(options =>
        {
            options.ApiKey = "key";
            options.MaxRetries = 11;
        });
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IDecisionClient>());
    }

    [Fact]
    public async Task AValueTheBinderCannotConvert_FailsWithAnInvalidOperationException()
    {
        var builder = NewBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Minos:ApiKey"] = "key",
            ["Minos:MaxRetries"] = "abc",
        });
        builder.Services.AddDecisionClient(builder.Configuration.GetSection("Minos"));
        using var host = builder.Build();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task TheNamedHttpClient_AndAReplacedClient_StillNeedValidOptions()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient(options =>
        {
            options.ApiKey = "key";
            options.MaxRetries = 11;
        });
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient("Minos.NET"));

        var builder = NewBuilder();
        using var http = new HttpClient();
        using var replacement = new DecisionClient(http, new DecisionClientOptions { ApiKey = "placeholder" });
        builder.Services.AddSingleton<IDecisionClient>(replacement);
        builder.Services.AddDecisionClient(options => options.MaxRetries = 11);
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task AnAppsOwnClientRegisteredFirst_Wins()
    {
        var builder = NewBuilder();
        using var http = new HttpClient();
        using var own = new DecisionClient(http, new DecisionClientOptions { ApiKey = "own" });
        builder.Services.AddSingleton<IDecisionClient>(own);
        builder.Services.AddDecisionClient(options => options.ApiKey = "placeholder");
        using var host = builder.Build();
        await host.StartAsync();

        Assert.Same(own, host.Services.GetRequiredService<IDecisionClient>());
    }

    [Fact]
    public async Task AHandlerOfYours_SeesTheRequest_FromTheBuilderOrTheDefaults()
    {
        var viaBuilder = new ServiceCollection();
        using var first = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        HandlerRegistration.AddTracedDecision(viaBuilder, "key").ConfigurePrimaryHttpMessageHandler(() => first);
        await using (var provider = viaBuilder.BuildServiceProvider())
        {
            await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        }

        var viaDefaults = new ServiceCollection();
        using var second = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        HandlerRegistration.AddTracedEverywhere(viaDefaults, "key");
        viaDefaults.AddHttpClient("Minos.NET").ConfigurePrimaryHttpMessageHandler(() => second);
        await using (var provider = viaDefaults.BuildServiceProvider())
        {
            await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        }

        var after = new ServiceCollection();
        using var third = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        after.AddTransient<TraceHeaderHandler>();
        after.AddDecisionClient(options => options.ApiKey = "key").HttpClient.ConfigurePrimaryHttpMessageHandler(() => third);
        after.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        await using (var provider = after.BuildServiceProvider())
        {
            await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        }

        Assert.Collection(first.Requests, sent => Assert.True(sent.HasTraceHeader));
        Assert.Collection(third.Requests, sent => Assert.True(sent.HasTraceHeader));
        Assert.Collection(second.Requests, sent => Assert.True(sent.HasTraceHeader));
    }

    [Fact]
    public async Task ClearingTheAdditionalHandlers_KeepsADefaultsHandlerOffTheMinosClient()
    {
        var withDefaults = new ServiceCollection();
        using var first = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        HandlerRegistration.AddTracedEverywhere(withDefaults, "key");
        withDefaults.AddHttpClient("Minos.NET").ConfigurePrimaryHttpMessageHandler(() => first);
        await using (var provider = withDefaults.BuildServiceProvider())
        {
            await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        }

        var cleared = new ServiceCollection();
        using var second = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        HandlerRegistration.AddDecisionWithoutDefaultHandlers(cleared, "key").ConfigurePrimaryHttpMessageHandler(() => second);
        await using (var provider = cleared.BuildServiceProvider())
        {
            await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        }

        Assert.Collection(first.Requests, sent => Assert.True(sent.HasTraceHeader));
        Assert.Collection(second.Requests, sent => Assert.False(sent.HasTraceHeader));
    }

    [Fact]
    public async Task ClearingTheAdditionalHandlers_AlsoClearsOnesAddedThroughTheBuilder()
    {
        var services = new ServiceCollection();
        using var stub = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        services.AddTransient<TraceHeaderHandler>();
        services.AddDecisionClient(options => options.ApiKey = "key")
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddHttpMessageHandler<TraceHeaderHandler>()
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);

        Assert.Collection(stub.Requests, sent => Assert.False(sent.HasTraceHeader));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADefaultsPrimaryHandlerAndLogger_NeverReachTheMinosClient(bool defaultsFirst)
    {
        var capture = new CategoryCapture();
        var defaultsPrimary = new CountingHandler();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        void Defaults() => services.ConfigureHttpClientDefaults(defaults =>
        {
            defaults.ConfigurePrimaryHttpMessageHandler(() => defaultsPrimary);
            defaults.AddDefaultLogger();
        });
        void AddMinos() => services.AddDecisionClient(options =>
        {
            options.ApiKey = "key";
            options.MaxRetries = 0;
            options.BaseAddress = new Uri("http://127.0.0.1:1/");
        });

        if (defaultsFirst)
        {
            Defaults();
            AddMinos();
        }
        else
        {
            AddMinos();
            Defaults();
        }

        await using var provider = services.BuildServiceProvider();

        // The client's own SocketsHttpHandler is used, so the call fails as a refused connection and the defaults' handler is untouched.
        var result = await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        using var control = provider.GetRequiredService<IHttpClientFactory>().CreateClient("other");
        using var controlResponse = await control.GetAsync(new Uri("http://other.example/"));

        Assert.Equal(DecisionErrorKind.Network, result.Error.Kind);
        Assert.Equal(1, defaultsPrimary.Count);
        Assert.Contains(capture.Categories, category => category.StartsWith("System.Net.Http.HttpClient.other", StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Categories, category => category.StartsWith("System.Net.Http.HttpClient.Minos.NET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheClientLogsItself_AndTheFactorysRequestLogsAreOffUnlessAskedFor()
    {
        var withoutFactoryLogs = await CategoriesLoggedAsync(addDefaultLogger: false);
        var withFactoryLogs = await CategoriesLoggedAsync(addDefaultLogger: true);

        Assert.Contains("Minos.DecisionClient", withoutFactoryLogs);
        Assert.DoesNotContain(withoutFactoryLogs, category => category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
        Assert.Contains(withFactoryLogs, category => category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigureHttpClient_SetsUpAnyHttpClient_WithoutAnApiKey()
    {
        using var environment = new KeyEnvironment(typeSafe: null, openRouter: null);
        var options = new DecisionClientOptions { Timeout = TimeSpan.FromSeconds(15) };
        var services = new ServiceCollection();
        WithoutThePackage.AddMinosHttpClient(services, options);
        using var provider = services.BuildServiceProvider();

        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("minos");

        Assert.Equal(TimeSpan.FromSeconds(15), http.Timeout);
        Assert.Equal(new Uri("https://api.typesafe.ai/"), http.BaseAddress);
        Assert.Contains(http.DefaultRequestHeaders.UserAgent, product => string.Equals(product.Product?.Name, "Minos.NET", StringComparison.Ordinal));
    }

    [Fact]
    public void ACreatedClient_UsesTheFactorysHttpClient()
    {
        var options = new DecisionClientOptions { ApiKey = "key" };
        var services = new ServiceCollection();
        WithoutThePackage.AddMinosHttpClient(services, options);
        using var provider = services.BuildServiceProvider();

        using var client = WithoutThePackage.Create(provider.GetRequiredService<IHttpClientFactory>(), options);

        Assert.NotNull(client);
    }

    private static HostApplicationBuilder NewBuilder()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(Keys);
        return builder;
    }

    private static string PageBlock(string language)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", "dependency-injection.md"));
        var start = Array.FindIndex(lines, line => string.Equals(line, "```" + language, StringComparison.Ordinal));
        Assert.True(start >= 0, "The page has a block of " + language);
        var end = Array.FindIndex(lines, start + 1, line => string.Equals(line, "```", StringComparison.Ordinal));
        return string.Join('\n', lines[(start + 1)..end]);
    }

    private static async Task<IReadOnlyCollection<string>> CategoriesLoggedAsync(bool addDefaultLogger)
    {
        var capture = new CategoryCapture();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        using var handler = new ScriptedDecision.Handler([Reply.Ok(UrgentResponse)]);
        var builder = services.AddDecisionClient(options => options.ApiKey = "key").HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        if (addDefaultLogger)
        {
            builder.AddDefaultLogger();
        }

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        Assert.True(result.IsSuccess);
        return capture.Categories;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class CategoryCapture : ILoggerProvider
    {
        private readonly HashSet<string> _categories = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Categories
        {
            get
            {
                lock (_categories)
                {
                    return [.. _categories];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capturing(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Capturing(CategoryCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._categories)
                {
                    owner._categories.Add(category);
                }
            }
        }
    }
}
