using System.Buffers;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Minos.Protocols;
using Minos.Serialization;
using Minos.Telemetry;
using Minos.Transport;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>Calls TypeSafe's Jev System One API, directly or through OpenRouter.</summary>
/// <remarks>
/// Thread-safe. Create one per application and reuse it; dispose it when the application stops. Pass an
/// <see cref="ILoggerFactory"/> to log each operation, each retried attempt and each unexpected exception; a neutral
/// <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> call is logged only with
/// <see cref="DecisionClientOptions.UseStandardPipeline"/>, while the raw System One calls are logged either way. What the
/// library writes never contains the state, questions, answers, API key, a header value or an error response body; the
/// unexpected-exception event carries the exception as thrown, which can include one from your own handler.
/// Spans and metrics come from the Minos ActivitySource and Meter; see the
/// <see href="https://marcelroozekrans.github.io/Minos.NET/observability">observability guide</see>.
/// <para>
/// Each <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> call runs the standard pipeline: one span and one
/// set of metrics over the whole call (<see cref="OpenTelemetryDecisionClient"/>), one outcome log when a logger factory
/// was given (<see cref="LoggingDecisionClient"/>), then retries (<see cref="RetryingDecisionClient"/>) around a transport
/// that sends one attempt. <see cref="DecisionClientOptions.UseStandardPipeline"/> set to <see langword="false"/> leaves the
/// transport alone. <see cref="GetService"/> finds each stage.
/// </para>
/// </remarks>
public sealed class DecisionClient : IDecisionClient
{
    private static readonly ProductInfoHeaderValue UserAgent = CreateUserAgent();

    private readonly DecisionOperationsInstrumented _operations;
    private readonly HttpClient? _ownedHttpClient;
    private readonly string _providerName;
    private readonly Uri _endpoint;
    private readonly DecisionProvider _provider;
    private readonly ILogger? _logger;
    private readonly DecisionClientMetadata _metadata;
    private readonly DecisionRetryOptions _retryOptions;
    private readonly IDecisionClient _pipeline;
    // Volatile: Dispose writes it before it disposes the owned HttpClient, and the error mapper, the transport and the raw
    // calls' retry loop read it on the threads that complete the calls in flight.
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecisionClient"/> class that creates and owns its
    /// <see cref="HttpClient"/>, using defaults and environment variables.
    /// </summary>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public DecisionClient()
        : this((DecisionClientOptions?)null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DecisionClient"/> class that creates and owns its <see cref="HttpClient"/>.</summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public DecisionClient(DecisionClientOptions? options)
        : this(ResolveSettings(options), httpClient: null, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DecisionClient"/> class over a caller-owned <see cref="HttpClient"/>,
    /// using defaults and environment variables.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="DecisionClientOptions.BaseAddress"/> when set, and must end in '/'; when it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="httpClient"/> already has an invalid base address.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public DecisionClient(HttpClient httpClient)
        : this(httpClient, (DecisionClientOptions?)null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DecisionClient"/> class over a caller-owned <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="DecisionClientOptions.BaseAddress"/> when set, and must end in '/'; when it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value, or <paramref name="httpClient"/> already has an invalid base address.
    /// </exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public DecisionClient(HttpClient httpClient, DecisionClientOptions? options)
        : this(ResolveSettings(httpClient, options), httpClient, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DecisionClient"/> class that creates and owns its <see cref="HttpClient"/>
    /// and logs through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <param name="loggerFactory">
    /// Creates the client's logger, in the <c>Minos.DecisionClient</c> category; <see langword="null"/> logs nothing.
    /// The client does not dispose it, so it must outlive the client.
    /// </param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public DecisionClient(DecisionClientOptions? options, ILoggerFactory? loggerFactory)
        : this(
            ResolveSettings(options),
            httpClient: null,
            ownedHandler: null,
            TimeProvider.System,
            ArrayPool<byte>.Shared,
            loggerFactory?.CreateLogger(DecisionLog.Category))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DecisionClient"/> class over a caller-owned <see cref="HttpClient"/> that
    /// logs through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="DecisionClientOptions.BaseAddress"/> when set, and must end in '/'; when it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <param name="loggerFactory">
    /// Creates the client's logger, in the <c>Minos.DecisionClient</c> category; <see langword="null"/> logs nothing.
    /// The client does not dispose it, so it must outlive the client.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value, or <paramref name="httpClient"/> already has an invalid base address.
    /// </exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public DecisionClient(HttpClient httpClient, DecisionClientOptions? options, ILoggerFactory? loggerFactory)
        : this(
            ResolveSettings(httpClient, options),
            httpClient,
            ownedHandler: null,
            TimeProvider.System,
            ArrayPool<byte>.Shared,
            loggerFactory?.CreateLogger(DecisionLog.Category))
    {
    }

    internal DecisionClient(DecisionClientSettings settings, HttpClient? httpClient, HttpMessageHandler? ownedHandler, TimeProvider time)
        : this(settings, httpClient, ownedHandler, time, ArrayPool<byte>.Shared)
    {
    }

    // pool supplies the typed path's request and response buffers; tests pass a counting pool to check every one is returned.
    internal DecisionClient(
        DecisionClientSettings settings,
        HttpClient? httpClient,
        HttpMessageHandler? ownedHandler,
        TimeProvider time,
        ArrayPool<byte> pool)
        : this(settings, httpClient, ownedHandler, time, pool, logger: null)
    {
    }

    // logger is null when no factory was given, and then nothing in the pipeline or the operations changes.
    internal DecisionClient(
        DecisionClientSettings settings,
        HttpClient? httpClient,
        HttpMessageHandler? ownedHandler,
        TimeProvider time,
        ArrayPool<byte> pool,
        ILogger? logger)
    {
        if (httpClient is null)
        {
            httpClient = new HttpClient(ownedHandler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
            ApplyHttpSettings(httpClient, new DecisionHttpSettings(settings.BaseAddress, settings.Timeout));
            _ownedHttpClient = httpClient;
        }
        else if (httpClient.BaseAddress is not null)
        {
            ValidateBorrowedBaseAddress(httpClient);
        }
        else
        {
            httpClient.BaseAddress = settings.BaseAddress;
        }

        _provider = settings.Provider;
        _retryOptions = new DecisionRetryOptions
        {
            MaxRetries = settings.MaxRetries,
            InitialBackoff = settings.InitialBackoff,
            MaxRetryDelay = settings.MaxRetryDelay,
            Jitter = settings.Jitter,
        };
        _providerName = DecisionTelemetry.ProviderOf(settings.Provider);
        _endpoint = httpClient.BaseAddress!;
        _logger = logger;
        // A disposed client never retries. Only an owned HttpClient is disposed with the client, so only an owned one can
        // tear a request down: the mapper reports such an attempt as Disposed, which is never retried. A borrowed
        // HttpClient's attempt in flight keeps its own result, so its mapper never reads the flag.
        Func<bool> disposed = () => _disposed;
        var api = new DecisionApiClient(
            httpClient,
            new SystemTextJsonSerializer(DecisionJsonContext.Default),
            new DecisionRawSerializer(pool),
            new DecisionErrorMapper(time, _ownedHttpClient is null ? null : disposed, SystemOneProtocol.Instance));
        _metadata = new DecisionClientMetadata(_providerName, _endpoint, settings.Model);
        var authorization = "Bearer " + settings.ApiKey;

        // The standard chain, outermost first: one span over every attempt, one outcome log inside it, then retries. Each
        // stage reads the metadata through GetService, which the chain answers from the transport at its bottom.
        IDecisionClient pipeline = new DecisionTransport(api, SystemOneProtocol.Instance, authorization, _metadata, pool, disposed);
        DecisionRetry? rawRetry = null;
        if (settings.UseStandardPipeline)
        {
            pipeline = new RetryingDecisionClient(pipeline, _retryOptions.Clone(), logger, time);
            if (logger is not null)
            {
                pipeline = new LoggingDecisionClient(pipeline, logger);
            }

            pipeline = new OpenTelemetryDecisionClient(pipeline);

            // The raw System One calls keep their own span and log, and retry through the same loop with the same settings.
            rawRetry = new DecisionRetry(_retryOptions.Clone(), logger, time, disposed);
        }

        _pipeline = pipeline;

        // Always wired: with nothing listening, the generated proxy returns each operation's own task.
        _operations = new DecisionOperationsInstrumented(new DecisionOperations(api, authorization, rawRetry, disposed));
    }

    /// <summary>
    /// Configures an <see cref="HttpClient"/> as <see cref="DecisionClient"/> configures one it creates, except its handler, for a client you then
    /// pass to a constructor that takes an <see cref="HttpClient"/>, such as one from <c>IHttpClientFactory</c>.
    /// </summary>
    /// <param name="httpClient">The client to configure. It must not have sent a request yet.</param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <remarks>
    /// Sets <see cref="HttpClient.BaseAddress"/> from <see cref="DecisionClientOptions.BaseAddress"/>, the
    /// <see cref="DecisionDefaults.BaseAddressEnvironmentVariable"/> environment variable or the provider's default, only when
    /// <paramref name="httpClient"/> has none. Sets <see cref="HttpClient.Timeout"/> to <see cref="DecisionClientOptions.Timeout"/>,
    /// the per-attempt time-out, and adds the <c>Minos.NET</c> User-Agent unless it is already there. It reads no
    /// API key, so it can run before one is configured.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The provider, the time-out or the base address option is invalid.</exception>
    /// <exception cref="InvalidOperationException">The base address environment variable is invalid, or <paramref name="httpClient"/> has already sent a request.</exception>
    public static void ConfigureHttpClient(HttpClient httpClient, DecisionClientOptions? options)
        => ConfigureHttpClient(httpClient, options, Environment.GetEnvironmentVariable);

    // environment reads an environment variable; tests pass a fake.
    internal static void ConfigureHttpClient(HttpClient httpClient, DecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ApplyHttpSettings(httpClient, DecisionClientSettings.ResolveHttp(options, environment));
    }

    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state and, optionally, the model.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state and, optionally, the model.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>
    /// Runs the client's pipeline: with <see cref="DecisionClientOptions.UseStandardPipeline"/>, telemetry, logging when
    /// the client has a logger factory, and retries with the options' retry settings; without it, one attempt. The
    /// request is sent with <see cref="DecisionClientOptions.Model"/> when <see cref="DecisionRequest.Model"/> is
    /// <see langword="null"/>, and every pooled buffer is returned before the call completes.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _pipeline.EvaluateAsync(request, cancellationToken);
    }

    // Explicit, so the public pair above follows the (x)/(x, CancellationToken) convention: implementing the interface's
    // optional parameter implicitly would either drop its default or give this class an overload with an optional one.
    ValueTask<Result<DecisionResponse, DecisionError>> IDecisionClient.EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken)
        => EvaluateAsync(request, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Returns this client for a type it is, a new <see cref="DecisionRetryOptions"/> from its retry settings, its
    /// <see cref="DecisionClientMetadata"/>, then whatever its pipeline's stages return, such as
    /// <see cref="RetryingDecisionClient"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="serviceType"/> is <see langword="null"/>.</exception>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null)
        {
            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType == typeof(DecisionRetryOptions))
            {
                return _retryOptions.Clone();
            }

            if (serviceType.IsInstanceOfType(_metadata))
            {
                return _metadata;
            }
        }

        return _pipeline.GetService(serviceType, serviceKey);
    }

    /// <summary>Asks Jev the request's questions about its state.</summary>
    /// <param name="request">The state, the questions and the model.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(SystemOneRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <summary>Asks Jev the request's questions about its state.</summary>
    /// <param name="request">The state, the questions and the model.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(SystemOneRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = DecisionLog.StartTiming(_logger);
        return WithLogging(
            _operations.EvaluateAsync(request, _providerName, _endpoint, cancellationToken),
            request.Model,
            _logger is null ? 0 : request.Questions is { } questions ? questions.Count : 0,
            started,
            cancellationToken);
    }

    /// <summary>Lists the models and aliases available to the account. TypeSafe's API only.</summary>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The models, or the <see cref="DecisionError"/> that prevented them; <see cref="DecisionErrorKind.Unsupported"/> on OpenRouter.</returns>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = DecisionLog.StartTiming(_logger);

        // Created before any call, so it bypasses the proxy: no request is sent, and no span or metric is recorded.
        if (_provider == DecisionProvider.OpenRouter)
        {
            return WithModelLogging(
                ValueTask.FromResult(Result<ModelList, DecisionError>.Failure(new DecisionError(
                    DecisionErrorKind.Unsupported,
                    "Model listing is only available on TypeSafe's API; OpenRouter has its own Models API."))),
                started,
                cancellationToken);
        }

        return WithModelLogging(_operations.ListModelsAsync(_providerName, _endpoint, cancellationToken), started, cancellationToken);
    }

    /// <summary>Disposes the client's pipeline and the <see cref="HttpClient"/> this client created; a borrowed one is left alone.</summary>
    /// <remarks>
    /// <para>
    /// A call started after <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>. A call already in flight
    /// over an <see cref="HttpClient"/> this client created is torn down with it, or is never sent when the disposal
    /// lands just before the send; either way it returns a <see cref="DecisionErrorKind.Disposed"/> failure. A real time-out
    /// that was mapped before <see cref="Dispose"/> set the flag stays <see cref="DecisionErrorKind.Timeout"/> when no retry
    /// is left.
    /// </para>
    /// <para>
    /// A disposed client never retries. A retry that would start after <see cref="Dispose"/> is not sent, and the call
    /// returns <see cref="DecisionErrorKind.Disposed"/>, whether the client created its <see cref="HttpClient"/> or borrowed
    /// it. A borrowed <see cref="HttpClient"/> is not disposed, so the attempt already in flight over it is not torn down
    /// and keeps its own result. Calling <see cref="Dispose"/> more than once does nothing.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Set first: disposing the HttpClient cancels the requests in flight, or makes a send that has not started throw
        // ObjectDisposedException, and the mapper, the transport and the retry loops must see the flag when either happens.
        _disposed = true;
        _pipeline.Dispose();
        _ownedHttpClient?.Dispose();
    }

    // Checked per call: without a logger, or with every level the operation can emit disabled, this returns call
    // itself, so the client runs exactly the unlogged code and pays no extra state machine.
    private ValueTask<Result<SystemOneResponse, DecisionError>> WithLogging(
        ValueTask<Result<SystemOneResponse, DecisionError>> call, string model, int questionCount, long started, CancellationToken ct)
        => _logger is { } logger && DecisionLog.IsAnyEnabled(logger)
            ? LogEvaluationAsync(logger, call, model, questionCount, started, ct)
            : call;

    // Logs the whole outcome once, after any retries; the filter logs a thrown exception without catching it, so it
    // surfaces unchanged.
    private async ValueTask<Result<SystemOneResponse, DecisionError>> LogEvaluationAsync(
        ILogger logger,
        ValueTask<Result<SystemOneResponse, DecisionError>> call,
        string model,
        int questionCount,
        long started,
        CancellationToken ct)
    {
        try
        {
            var result = await call.ConfigureAwait(false);
            var durationMs = DecisionLog.ElapsedMilliseconds(started);
            if (result.IsSuccess)
            {
                var providerName = ProviderName(_provider);
                DecisionLog.EvaluationSucceeded(logger, DecisionLog.Evaluate, model, providerName, questionCount, durationMs);
            }
            else
            {
                var error = result.Error;
                var message = DecisionLog.SafeMessage(error);
                DecisionLog.EvaluationFailed(logger, DecisionLog.Evaluate, model, error.Kind, error.StatusCode, durationMs, message);
            }

            return result;
        }
        catch (Exception exception) when (DecisionLog.LogUnexpected(logger, DecisionLog.Evaluate, exception, ct))
        {
            throw;
        }
    }

    private static string ProviderName(DecisionProvider provider)
        => provider == DecisionProvider.OpenRouter ? nameof(DecisionProvider.OpenRouter) : nameof(DecisionProvider.TypeSafe);

    private ValueTask<Result<ModelList, DecisionError>> WithModelLogging(
        ValueTask<Result<ModelList, DecisionError>> call, long started, CancellationToken ct)
        => _logger is { } logger && DecisionLog.IsAnyEnabled(logger) ? LogModelsAsync(logger, call, started, ct) : call;

    private async ValueTask<Result<ModelList, DecisionError>> LogModelsAsync(
        ILogger logger, ValueTask<Result<ModelList, DecisionError>> call, long started, CancellationToken ct)
    {
        try
        {
            var result = await call.ConfigureAwait(false);
            var durationMs = DecisionLog.ElapsedMilliseconds(started);
            if (result.IsSuccess)
            {
                var modelCount = result.Value.Models.Count;
                DecisionLog.ModelsListed(logger, _provider, modelCount, durationMs);
            }
            else
            {
                var error = result.Error;
                var message = DecisionLog.SafeMessage(error);
                DecisionLog.ModelsListFailed(logger, _provider, error.Kind, error.StatusCode, durationMs, message);
            }

            return result;
        }
        catch (Exception exception) when (DecisionLog.LogUnexpected(logger, DecisionLog.ListModels, exception, ct))
        {
            throw;
        }
    }

    // Validates a caller-supplied HttpClient.BaseAddress with the same rules as a configured address. The caller's
    // client is never modified, so unlike a configured address (which gets a trailing slash appended), the path here
    // must already end in '/'.
    private static void ValidateBorrowedBaseAddress(HttpClient httpClient)
    {
        var baseAddress = httpClient.BaseAddress!;

        if (!baseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("httpClient.BaseAddress must be an absolute URI.", nameof(httpClient));
        }

        if (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("httpClient.BaseAddress must use the http or https scheme.", nameof(httpClient));
        }

        if (baseAddress.Query.Length > 0 || baseAddress.Fragment.Length > 0)
        {
            throw new ArgumentException("httpClient.BaseAddress must not contain a query or fragment.", nameof(httpClient));
        }

        if (!baseAddress.AbsoluteUri.EndsWith('/'))
        {
            throw new ArgumentException("httpClient.BaseAddress must end with '/'.", nameof(httpClient));
        }
    }

    // What an owned HttpClient gets and ConfigureHttpClient applies: the base address when there is none, the per-attempt
    // time-out, and the User-Agent once.
    private static void ApplyHttpSettings(HttpClient httpClient, DecisionHttpSettings settings)
    {
        // Timeout first: its setter can throw, and nothing else has been changed by then.
        httpClient.Timeout = settings.Timeout;
        httpClient.BaseAddress ??= settings.BaseAddress;
        if (!httpClient.DefaultRequestHeaders.UserAgent.Contains(UserAgent))
        {
            httpClient.DefaultRequestHeaders.UserAgent.Add(UserAgent);
        }
    }

    private static DecisionClientSettings ResolveSettings(DecisionClientOptions? options)
        => DecisionClientSettings.Resolve(options, Environment.GetEnvironmentVariable);

    // Validates httpClient before resolving settings, so an invalid options value never masks a null httpClient.
    private static DecisionClientSettings ResolveSettings(HttpClient httpClient, DecisionClientOptions? options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return ResolveSettings(options);
    }

    // ProductInfoHeaderValue's constructor throws FormatException for a version that is not a valid product token;
    // a User-Agent header must never prevent client construction, so this falls back to "0.0.0" instead.
    private static ProductInfoHeaderValue CreateUserAgent()
    {
        try
        {
            return new ProductInfoHeaderValue("Minos.NET", ClientVersion());
        }
        catch (FormatException)
        {
            return new ProductInfoHeaderValue("Minos.NET", "0.0.0");
        }
    }

    private static string ClientVersion()
    {
        var version = typeof(DecisionClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
        {
            return "0.0.0";
        }

        // Strip build metadata (e.g. a source-control commit hash appended after '+' by the build), which is not
        // part of the version a caller would want to see or compare in a User-Agent header.
        var buildMetadata = version.IndexOf('+', StringComparison.Ordinal);
        return buildMetadata < 0 ? version : version[..buildMetadata];
    }
}
