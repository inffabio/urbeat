using FluentAssertions;
using Urbeat.WebApi.Infrastructure;

namespace Urbeat.UnitTests.WebApi;

public sealed class RateLimitingOptionsValidatorTests
{
    private readonly RateLimitingOptionsValidator _sut = new();

    [Fact]
    public void Validate_ShouldSucceed_ForTheDefaultResendBucket()
        => _sut.Validate(null, new RateLimitingOptions()).Succeeded.Should().BeTrue();

    [Theory]
    [InlineData(0, 120)]
    [InlineData(-1, 120)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    public void Validate_ShouldFail_WhenTheAuthoritativeResendBucketIsNotPositive(
        int permitLimit,
        int windowSeconds)
    {
        var options = new RateLimitingOptions
        {
            EmailResendPermitLimit = permitLimit,
            EmailResendWindowSeconds = windowSeconds,
        };

        var result = _sut.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(
            permitLimit < 1
                ? "EmailResendPermitLimit"
                : "EmailResendWindowSeconds");
    }
}
