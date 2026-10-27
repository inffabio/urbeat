using FluentAssertions;
using Urbeat.Domain.Services;

namespace Urbeat.UnitTests.Domain;

public sealed class BrazilianMobilePhoneTests
{
    [Theory]
    [InlineData("11999998888", "11999998888")]
    [InlineData("(11) 99999-8888", "11999998888")]
    [InlineData("11 9 9999-8888", "11999998888")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalize_ShouldKeepOnlyDigits(string? value, string expected)
    {
        BrazilianMobilePhone.Normalize(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("11999998888")]
    [InlineData("(11) 99999-8888")]
    [InlineData(" 11 9 9999 8888 ")]
    public void IsValid_ShouldAcceptMaskedAndUnmaskedMobile(string value)
    {
        BrazilianMobilePhone.IsValid(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1199999888")] // landline has 10 digits
    [InlineData("119999988889")] // too many digits
    [InlineData("00999998888")] // invalid DDD
    [InlineData("10999998888")] // invalid DDD
    [InlineData("11899998888")] // mobile subscriber must start with 9
    [InlineData("(11) 9999-8888")] // legacy landline
    [InlineData("11999A98888")] // letters are not accepted
    [InlineData("(11) 99999-8888#")] // unknown separator
    public void IsValid_ShouldRejectInvalidValues(string value)
    {
        BrazilianMobilePhone.IsValid(value).Should().BeFalse();
    }
}
