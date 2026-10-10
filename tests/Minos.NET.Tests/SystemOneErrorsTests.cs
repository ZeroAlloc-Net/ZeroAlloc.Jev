using Minos.Protocols;

namespace Minos.Tests;

public sealed class SystemOneErrorsTests
{
    [Theory]
    [InlineData(400, DecisionErrorKind.Validation)]
    [InlineData(401, DecisionErrorKind.Unauthorized)]
    [InlineData(403, DecisionErrorKind.Unauthorized)]
    [InlineData(404, DecisionErrorKind.Http)]
    [InlineData(408, DecisionErrorKind.Http)]
    [InlineData(422, DecisionErrorKind.Validation)]
    [InlineData(429, DecisionErrorKind.RateLimited)]
    [InlineData(500, DecisionErrorKind.Server)]
    [InlineData(503, DecisionErrorKind.Overloaded)]
    [InlineData(529, DecisionErrorKind.Overloaded)]
    public void Status_maps_to_kind_and_message(int status, DecisionErrorKind kind)
    {
        var error = SystemOneProtocol.Instance.MapError(status, default, bodyTruncated: false, contentType: null, retryAfter: TimeSpan.FromSeconds(3));

        Assert.Equal(kind, error.Kind);
        Assert.Equal($"The API returned HTTP {status}.", error.Message);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(3), error.RetryAfter);
        Assert.Null(error.Detail);
    }

    [Fact]
    public void Json_body_becomes_detail()
    {
        var error = SystemOneProtocol.Instance.MapError(422, """{"error":"bad"}"""u8, false, "application/problem+json", null);

        Assert.Equal("bad", error.Detail!.Value.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("text/plain", false)]
    [InlineData("application/json", true)]
    public void Truncated_or_non_json_bodies_have_no_detail(string contentType, bool truncated)
        => Assert.Null(SystemOneProtocol.Instance.MapError(500, """{"a":1}"""u8, truncated, contentType, null).Detail);

    [Fact]
    public void Trailing_content_after_the_json_body_has_no_detail()
        => Assert.Null(SystemOneProtocol.Instance.MapError(500, """{"a":1} x"""u8, false, "application/json", null).Detail);

    [Fact]
    public void Invalid_json_body_has_no_detail()
        => Assert.Null(SystemOneProtocol.Instance.MapError(500, "{oops"u8, false, "application/json", null).Detail);
}
