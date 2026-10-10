namespace Minos.Tests;

public sealed class DecisionRetryOptionsTests
{
    [Fact]
    public void The_defaults()
    {
        var options = new DecisionRetryOptions();

        Assert.Equal(2, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxRetryDelay);
        Assert.True(options.Jitter);
        Assert.Equal(DecisionRetryOptions.IsTransient, options.ShouldRetry);
    }

    [Theory]
    [InlineData(DecisionErrorKind.RateLimited, null, true)]
    [InlineData(DecisionErrorKind.Overloaded, null, true)]
    [InlineData(DecisionErrorKind.Server, null, true)]
    [InlineData(DecisionErrorKind.Network, null, true)]
    [InlineData(DecisionErrorKind.Timeout, null, true)]
    [InlineData(DecisionErrorKind.Http, 408, true)]
    [InlineData(DecisionErrorKind.Http, 400, false)]
    [InlineData(DecisionErrorKind.Http, null, false)]
    [InlineData(DecisionErrorKind.Validation, null, false)]
    [InlineData(DecisionErrorKind.Unauthorized, 401, false)]
    [InlineData(DecisionErrorKind.InvalidResponse, null, false)]
    [InlineData(DecisionErrorKind.Disposed, null, false)]
    public void IsTransient_by_kind(DecisionErrorKind kind, int? status, bool expected)
        => Assert.Equal(expected, DecisionRetryOptions.IsTransient(new DecisionError(kind, "x") { StatusCode = status }));

    [Fact]
    public void IsTransient_covers_every_kind()
    {
        foreach (var kind in Enum.GetValues<DecisionErrorKind>())
        {
            var expected = kind is DecisionErrorKind.RateLimited or DecisionErrorKind.Overloaded or DecisionErrorKind.Server
                or DecisionErrorKind.Network or DecisionErrorKind.Timeout;
            Assert.Equal(expected, DecisionRetryOptions.IsTransient(new DecisionError(kind, "x")));
        }
    }

    [Fact]
    public void IsTransient_rejects_null()
        => Assert.Throws<ArgumentNullException>(() => DecisionRetryOptions.IsTransient(null!));

    [Fact]
    public void Defaults_are_valid()
        => new DecisionRetryOptions().Validate();

    [Theory]
    [InlineData(-1, 500, 30000, "MaxRetries must be between 0 and 10.")]
    [InlineData(11, 500, 30000, "MaxRetries must be between 0 and 10.")]
    [InlineData(2, 0, 30000, "InitialBackoff must be positive and at most int.MaxValue milliseconds.")]
    [InlineData(2, -5, 30000, "InitialBackoff must be positive and at most int.MaxValue milliseconds.")]
    [InlineData(2, 500, 499, "MaxRetryDelay must be at least InitialBackoff and at most int.MaxValue milliseconds.")]
    public void Validate_rejects_bad_numbers(int maxRetries, int backoffMs, int maxDelayMs, string message)
    {
        var options = new DecisionRetryOptions
        {
            MaxRetries = maxRetries,
            InitialBackoff = TimeSpan.FromMilliseconds(backoffMs),
            MaxRetryDelay = TimeSpan.FromMilliseconds(maxDelayMs),
        };

        var exception = Assert.Throws<ArgumentException>(options.Validate);

        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
        Assert.Equal("DecisionRetryOptions", exception.ParamName);
    }

    [Fact]
    public void Validate_rejects_a_null_should_retry()
    {
        var options = new DecisionRetryOptions { ShouldRetry = null! };

        var exception = Assert.Throws<ArgumentNullException>(options.Validate);

        Assert.Equal("ShouldRetry", exception.ParamName);
    }

    [Fact]
    public void Clone_copies_every_property_into_a_new_instance()
    {
        Func<DecisionError, bool> should = _ => true;
        var original = new DecisionRetryOptions
        {
            MaxRetries = 7,
            InitialBackoff = TimeSpan.FromMilliseconds(11),
            MaxRetryDelay = TimeSpan.FromSeconds(3),
            Jitter = false,
            ShouldRetry = should,
        };

        var copy = original.Clone();

        Assert.NotSame(original, copy);
        Assert.Equal(7, copy.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(11), copy.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(3), copy.MaxRetryDelay);
        Assert.False(copy.Jitter);
        Assert.Same(should, copy.ShouldRetry);
    }

    [Fact]
    public void A_decision_client_offers_a_fresh_copy_of_its_settings()
    {
        using var client = new DecisionClient(new DecisionClientOptions { ApiKey = "minos-k", MaxRetries = 4, Jitter = false });

        var first = client.GetService<DecisionRetryOptions>();
        var second = client.GetService<DecisionRetryOptions>();

        Assert.NotNull(first);
        Assert.NotSame(first, second);
        Assert.Equal(4, first.MaxRetries);
        Assert.False(first.Jitter);
        first.MaxRetries = 1;
        Assert.Equal(4, client.GetService<DecisionRetryOptions>()!.MaxRetries);
    }
}
