using System.Net;
using System.Text;
using System.Text.Json;
using Minos.Protocols;
using Minos.Transport;
using ZeroAlloc.Rest;

namespace Minos.Tests;

public sealed class DecisionErrorMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly DecisionErrorMapper Mapper = new(new FixedTimeProvider(Now), disposed: null, SystemOneProtocol.Instance);

    [Theory]
    [InlineData(401, DecisionErrorKind.Unauthorized)]
    [InlineData(403, DecisionErrorKind.Unauthorized)]
    [InlineData(400, DecisionErrorKind.Validation)]
    [InlineData(422, DecisionErrorKind.Validation)]
    [InlineData(429, DecisionErrorKind.RateLimited)]
    [InlineData(503, DecisionErrorKind.Overloaded)]
    [InlineData(529, DecisionErrorKind.Overloaded)]
    [InlineData(500, DecisionErrorKind.Server)]
    [InlineData(502, DecisionErrorKind.Server)]
    [InlineData(404, DecisionErrorKind.Http)]
    [InlineData(409, DecisionErrorKind.Http)]
    public void Status_MapsToKind_AndKeepsStatusCode(int status, DecisionErrorKind expected)
    {
        var error = Mapper.Map(Status(status));

        Assert.Equal(expected, error.Kind);
        Assert.Equal(status, error.StatusCode);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeout_MapsToTimeout_WithException()
    {
        var cause = new TaskCanceledException("timed out");

        var error = Mapper.Map(new HttpError((HttpStatusCode)0, Headers(), cause.Message) { Kind = HttpErrorKind.Timeout, Exception = cause });

        Assert.Equal(DecisionErrorKind.Timeout, error.Kind);
        Assert.Null(error.StatusCode);
        Assert.Same(cause, error.Exception);
    }

    [Fact]
    public void Transport_MapsToNetwork_WithException()
    {
        var cause = new HttpRequestException("connection refused");

        var error = Mapper.Map(new HttpError((HttpStatusCode)0, Headers(), cause.Message) { Kind = HttpErrorKind.Transport, Exception = cause });

        Assert.Equal(DecisionErrorKind.Network, error.Kind);
        Assert.Null(error.StatusCode);
        Assert.Equal("connection refused", error.Message);
        Assert.Same(cause, error.Exception);
    }

    // Disposing an owned client cancels its requests in flight; the attempt arrives as a time-out or a transport failure.
    [Fact]
    public void TimeoutOrTransport_AfterTheOwningClientWasDisposed_IsDisposed_WithException()
    {
        var mapper = new DecisionErrorMapper(new FixedTimeProvider(Now), disposed: () => true, SystemOneProtocol.Instance);
        var cancelled = new TaskCanceledException("cancelled");
        var refused = new HttpRequestException("connection refused");

        var timeout = mapper.Map(new HttpError((HttpStatusCode)0, Headers(), cancelled.Message) { Kind = HttpErrorKind.Timeout, Exception = cancelled });
        var transport = mapper.Map(new HttpError((HttpStatusCode)0, Headers(), refused.Message) { Kind = HttpErrorKind.Transport, Exception = refused });

        Assert.Equal(DecisionErrorKind.Disposed, timeout.Kind);
        Assert.Equal("The client was disposed while the request was in flight.", timeout.Message);
        Assert.Null(timeout.StatusCode);
        Assert.Same(cancelled, timeout.Exception);
        Assert.Equal(DecisionErrorKind.Disposed, transport.Kind);
        Assert.Same(refused, transport.Exception);
        Assert.False(IDecisionApi.IsTransient(timeout));
    }

    // The flag is read when the failure is mapped: a time-out mapped before the disposal stays Timeout.
    [Fact]
    public void Timeout_MappedBeforeTheDisposal_StaysTimeout_AndOneMappedAfterIsDisposed()
    {
        var disposed = false;
        var mapper = new DecisionErrorMapper(new FixedTimeProvider(Now), () => disposed, SystemOneProtocol.Instance);
        var cause = new TaskCanceledException("timed out");
        var failure = new HttpError((HttpStatusCode)0, Headers(), cause.Message) { Kind = HttpErrorKind.Timeout, Exception = cause };

        var before = mapper.Map(failure);
        disposed = true;
        var after = mapper.Map(failure);

        Assert.Equal(DecisionErrorKind.Timeout, before.Kind);
        Assert.Equal(DecisionErrorKind.Disposed, after.Kind);
    }

    // A response arrived, so nothing was torn down: its own kind stands even after the disposal.
    [Fact]
    public void StatusOrDeserialization_AfterTheOwningClientWasDisposed_KeepsItsKind()
    {
        var mapper = new DecisionErrorMapper(new FixedTimeProvider(Now), disposed: () => true, SystemOneProtocol.Instance);
        var cause = new JsonException("bad json");

        Assert.Equal(DecisionErrorKind.Overloaded, mapper.Map(Status(503)).Kind);
        Assert.Equal(
            DecisionErrorKind.InvalidResponse,
            mapper.Map(new HttpError(HttpStatusCode.OK, Headers(), cause.Message) { Kind = HttpErrorKind.Deserialization, Exception = cause }).Kind);
    }

    [Fact]
    public void Deserialization_MapsToInvalidResponse_KeepingStatus()
    {
        var cause = new JsonException("bad json");

        var error = Mapper.Map(new HttpError(HttpStatusCode.OK, Headers(), cause.Message) { Kind = HttpErrorKind.Deserialization, Exception = cause });

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(200, error.StatusCode);
        Assert.Same(cause, error.Exception);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData(" 7 ", 7)]
    [InlineData("0", 0)]
    public void RetryAfter_DeltaSeconds_IsParsed(string header, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), Mapper.Map(Status(429, retryAfter: header)).RetryAfter);

    [Fact]
    public void RetryAfter_HttpDate_IsRelativeToNow()
        => Assert.Equal(TimeSpan.FromSeconds(30), Mapper.Map(Status(429, retryAfter: "Sun, 27 Sep 2026 12:00:30 GMT")).RetryAfter);

    [Fact]
    public void RetryAfter_PastDate_IsZero()
        => Assert.Equal(TimeSpan.Zero, Mapper.Map(Status(503, retryAfter: "Sun, 27 Sep 2026 11:00:00 GMT")).RetryAfter);

    [Theory]
    [InlineData(null)]
    [InlineData("soon")]
    [InlineData("-3")]
    public void RetryAfter_AbsentOrInvalid_IsNull(string? header)
        => Assert.Null(Mapper.Map(Status(429, retryAfter: header)).RetryAfter);

    [Fact]
    public void RetryAfter_HeaderNameIsCaseInsensitive()
    {
        var headers = new Dictionary<string, IReadOnlyList<string>> { ["retry-after"] = ["4"] };

        var error = Mapper.Map(new HttpError(HttpStatusCode.TooManyRequests, headers, null));

        Assert.Equal(TimeSpan.FromSeconds(4), error.RetryAfter);
    }

    [Theory]
    [InlineData("Sunday, 27-Sep-26 12:00:30 GMT")]
    [InlineData("Sun Sep 27 12:00:30 2026")]
    [InlineData("sun, 27 sep 2026 12:00:30 gmt")]
    public void RetryAfter_OtherHttpDateForms_AreParsed(string header)
        => Assert.Equal(TimeSpan.FromSeconds(30), Mapper.Map(Status(429, retryAfter: header)).RetryAfter);

    [Fact]
    public void RetryAfter_DeltaSecondsOverflow_IsClamped()
        => Assert.Equal(RetryAfterHeader.MaxDelay, Mapper.Map(Status(429, retryAfter: "99999999999999999999")).RetryAfter);

    [Fact]
    public void RetryAfter_DeltaSecondsAtMaxDelayBoundary_IsExact()
        => Assert.Equal(TimeSpan.FromSeconds(2147483), Mapper.Map(Status(429, retryAfter: "2147483")).RetryAfter);

    [Fact]
    public void RetryAfter_DeltaSecondsJustOverMaxDelayBoundary_IsClamped()
        => Assert.Equal(RetryAfterHeader.MaxDelay, Mapper.Map(Status(429, retryAfter: "2147484")).RetryAfter);

    [Fact]
    public void RetryAfter_Rfc850TwoDigitYear_RollsOverRelativeToNow()
        // .NET's invariant calendar has a fixed TwoDigitYearMax of 2049, which would read "50" as 1950. RFC
        // 9110 section 5.6.7 instead ties the pivot to `now`: with Now = 2026, "50" names 2050 (far enough
        // ahead to clamp to MaxDelay), not 1950 (which would be in the past and yield TimeSpan.Zero).
        => Assert.Equal(RetryAfterHeader.MaxDelay, RetryAfterHeader.Parse("Saturday, 01-Jan-50 12:00:00 GMT", Now));

    [Theory]
    [InlineData("1500", 1500)]
    [InlineData("250.5", 250.5)]
    [InlineData("0", 0)]
    public void RetryAfterMs_IsParsed(string header, double milliseconds)
        => Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), Mapper.Map(WithHeaders(429, ("retry-after-ms", header))).RetryAfter);

    [Fact]
    public void RetryAfterMs_WinsOverRetryAfter()
        => Assert.Equal(TimeSpan.FromMilliseconds(200), Mapper.Map(WithHeaders(429, ("Retry-After", "5"), ("retry-after-ms", "200"))).RetryAfter);

    [Theory]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("infinity")]
    public void RetryAfterMs_Invalid_FallsBackToRetryAfter(string header)
        => Assert.Equal(TimeSpan.FromSeconds(5), Mapper.Map(WithHeaders(429, ("Retry-After", "5"), ("retry-after-ms", header))).RetryAfter);

    private static HttpError WithHeaders(int status, params (string Name, string Value)[] headers)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers)
        {
            map[name] = [value];
        }

        return new HttpError((HttpStatusCode)status, map, null) { Kind = HttpErrorKind.Status };
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/problem+json")]
    public void Detail_JsonBody_IsParsed(string contentType)
    {
        var error = Mapper.Map(Status(422, body: """{"detail":"questions.x.instructions is required"}""", contentType: contentType));

        Assert.NotNull(error.Detail);
        Assert.Equal("questions.x.instructions is required", error.Detail.Value.GetProperty("detail").GetString());
    }

    [Fact]
    public void Detail_NonJsonBody_IsNull()
        => Assert.Null(Mapper.Map(Status(422, body: "bad request", contentType: "text/plain")).Detail);

    [Fact]
    public void Detail_TruncatedBody_IsNull()
        => Assert.Null(Mapper.Map(Status(422, body: """{"detail":"x"}""", contentType: "application/json", truncated: true)).Detail);

    [Fact]
    public void Detail_MalformedJson_IsNull()
        => Assert.Null(Mapper.Map(Status(422, body: "{not json", contentType: "application/json")).Detail);

    // Bodies TypeSafe really sent, captured by the Live smoke workflow's run 37913354273 on 2026-10-09.
    private const string TypeSafeValidationBody = """{"detail":[{"type":"too_short","loc":["body","questions"],"msg":"Dictionary should have at least 1 item after validation, not 0","input":{},"ctx":{"field_type":"Dictionary","min_length":1,"actual_length":0}}]}""";

    private const string TypeSafeUnauthorizedBody = """{"detail":{"error_type":"authentication_error","message":"Cannot authenticate with the server. Please check your API key and try again."}}""";

    [Fact]
    public void TypeSafesValidationBody_IsKeptAsDetail_WithEachProblemsLocationAndMessage()
    {
        var error = Mapper.Map(Status(422, body: TypeSafeValidationBody, contentType: "application/json"));

        Assert.Equal(DecisionErrorKind.Validation, error.Kind);
        Assert.Equal(422, error.StatusCode);
        Assert.Null(error.RetryAfter);
        Assert.NotNull(error.Detail);

        var problems = error.Detail.Value.GetProperty("detail");
        Assert.Equal(JsonValueKind.Array, problems.ValueKind);
        Assert.Equal(1, problems.GetArrayLength());
        Assert.Equal("too_short", problems[0].GetProperty("type").GetString());
        Assert.Equal("""["body","questions"]""", problems[0].GetProperty("loc").GetRawText());
        Assert.Equal("Dictionary should have at least 1 item after validation, not 0", problems[0].GetProperty("msg").GetString());
    }

    [Fact]
    public void TypeSafesUnauthorizedBody_IsKeptAsDetail_AsAnObjectNotAList()
    {
        var error = Mapper.Map(Status(401, body: TypeSafeUnauthorizedBody, contentType: "application/json"));

        Assert.Equal(DecisionErrorKind.Unauthorized, error.Kind);
        Assert.Equal(401, error.StatusCode);
        Assert.Null(error.RetryAfter);
        Assert.NotNull(error.Detail);

        var detail = error.Detail.Value.GetProperty("detail");
        Assert.Equal(JsonValueKind.Object, detail.ValueKind);
        Assert.Equal("authentication_error", detail.GetProperty("error_type").GetString());
    }

    [Fact]
    public void ToString_WithStatus_IncludesKindAndStatus()
        => Assert.StartsWith("RateLimited (429): ", Mapper.Map(Status(429)).ToString(), StringComparison.Ordinal);

    [Fact]
    public void ToString_WithoutStatus_IncludesKind()
        => Assert.Equal("Unsupported: not here", new DecisionError(DecisionErrorKind.Unsupported, "not here").ToString());

    private static HttpError Status(
        int status,
        string? retryAfter = null,
        string? body = null,
        string? contentType = null,
        bool truncated = false)
    {
        var headers = retryAfter is null
            ? Headers()
            : new Dictionary<string, IReadOnlyList<string>> { ["Retry-After"] = [retryAfter] };

        return new HttpError((HttpStatusCode)status, headers, null)
        {
            Kind = HttpErrorKind.Status,
            Body = body is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(body),
            ContentType = contentType,
            BodyTruncated = truncated,
        };
    }

    [Fact]
    public void DecisionError_OfAnyOtherKind_HasNoFailures()
        => Assert.Empty(new DecisionError(DecisionErrorKind.Validation, "bad").Failures);

    [Fact]
    public void DecisionError_InitProperties_RoundTrip()
    {
        var exception = new InvalidOperationException("boom");
        using var document = JsonDocument.Parse("{\"field\":\"x\"}");
        var detail = document.RootElement.Clone();

        var error = new DecisionError(DecisionErrorKind.Validation, "bad")
        {
            StatusCode = 422,
            RetryAfter = TimeSpan.FromSeconds(3),
            Detail = detail,
            Exception = exception,
        };

        Assert.Equal(DecisionErrorKind.Validation, error.Kind);
        Assert.Equal("bad", error.Message);
        Assert.Equal(422, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(3), error.RetryAfter);
        Assert.Equal("x", error.Detail?.GetProperty("field").GetString());
        Assert.Same(exception, error.Exception);
        Assert.Empty(error.Failures);
    }

    [Fact]
    public void DecisionError_WithoutInitProperties_LeavesThemNull()
    {
        var error = new DecisionError(DecisionErrorKind.Network, "down");

        Assert.Null(error.StatusCode);
        Assert.Null(error.RetryAfter);
        Assert.Null(error.Detail);
        Assert.Null(error.Exception);
    }

    private static Dictionary<string, IReadOnlyList<string>> Headers() => [];
}
