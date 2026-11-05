using FluentAssertions;
using Urbeat.Application.SystemParameters;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Application.SystemParameters;

public sealed class SystemParameterValidatorTests
{
    [Theory]
    [InlineData("19.90", "19.90")]
    [InlineData("0", "0")]
    [InlineData("2.5", "2.5")]
    [InlineData("19.9", "19.9")]
    public void Decimal_ShouldNormalizeInvariantValues(string raw, string expected)
    {
        var policy = SystemParameterCatalog.Resolve("Store.DefaultDeliveryFee", SystemParameterType.Decimal, "Business");

        var valid = SystemParameterValidator.TryNormalize(policy, SystemParameterType.Decimal, raw, out var normalized, out var error);

        valid.Should().BeTrue(error);
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("R$ 19,90")]
    [InlineData("19,90")]
    [InlineData("1,000.50")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Decimal_ShouldRejectFormattedOrNonFiniteValues(string raw)
    {
        var policy = SystemParameterCatalog.Resolve("Store.DefaultDeliveryFee", SystemParameterType.Decimal, "Business");

        var valid = SystemParameterValidator.TryNormalize(policy, SystemParameterType.Decimal, raw, out _, out var error);

        valid.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Decimal_ShouldEnforcePolicyRange()
    {
        var policy = SystemParameterCatalog.Resolve("Store.DefaultDeliveryFee", SystemParameterType.Decimal, "Business");

        var valid = SystemParameterValidator.TryNormalize(policy, SystemParameterType.Decimal, "-1", out _, out var error);

        valid.Should().BeFalse();
        error.Should().Contain("entre");
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("587", "587")]
    public void Int32_ShouldNormalizeValidValues(string raw, string expected)
    {
        var policy = SystemParameterCatalog.Resolve("Email.SmtpPort", SystemParameterType.Int32, "Email");

        var valid = SystemParameterValidator.TryNormalize(policy, SystemParameterType.Int32, raw, out var normalized, out var error);

        valid.Should().BeTrue(error);
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("58.7")]
    [InlineData("abc")]
    [InlineData("65536")]
    [InlineData("-1")]
    public void Int32_ShouldRejectNonIntegersAndOutOfRangeValues(string raw)
    {
        var policy = SystemParameterCatalog.Resolve("Email.SmtpPort", SystemParameterType.Int32, "Email");

        var valid = SystemParameterValidator.TryNormalize(policy, SystemParameterType.Int32, raw, out _, out var error);

        valid.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("TRUE", "true")]
    [InlineData("1", "true")]
    [InlineData("false", "false")]
    [InlineData("0", "false")]
    public void Boolean_ShouldNormalizeAcceptedRepresentations(string raw, string expected)
    {
        var policy = SystemParameterCatalog.Resolve("Cookie.Secure", SystemParameterType.Boolean, "Cookie");

        var valid = SystemParameterValidator.TryNormalize(policy, SystemParameterType.Boolean, raw, out var normalized, out var error);

        valid.Should().BeTrue(error);
        normalized.Should().Be(expected);
    }

    [Fact]
    public void Boolean_ShouldRejectOtherValues()
    {
        var policy = SystemParameterCatalog.Resolve("Cookie.Secure", SystemParameterType.Boolean, "Cookie");

        var valid = SystemParameterValidator.TryNormalize(policy, SystemParameterType.Boolean, "yes", out _, out var error);

        valid.Should().BeFalse();
        error.Should().Contain("true");
    }

    [Fact]
    public void String_ShouldRejectValuesAboveMaxLengthWithoutTruncating()
    {
        var policy = SystemParameterCatalog.Resolve("Payment.Currency", SystemParameterType.String, "Payment");

        var valid = SystemParameterValidator.TryNormalize(
            policy,
            SystemParameterType.String,
            "ABCDE",
            out var normalized,
            out var error);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
        error.Should().Contain(policy.MaxLength.ToString());
    }

    [Fact]
    public void Url_ShouldAcceptAbsoluteHttpUrlsAndRejectRelativeOnes()
    {
        var policy = SystemParameterCatalog.Resolve("Url.FrontendBaseUrl", SystemParameterType.String, "Url");

        SystemParameterValidator.TryNormalize(policy, SystemParameterType.String, "https://urbeat.com.br", out _, out var validError)
            .Should().BeTrue(validError);
        SystemParameterValidator.TryNormalize(policy, SystemParameterType.String, "/painel", out _, out var invalidError)
            .Should().BeFalse();
        invalidError.Should().Contain("URL");
    }

    [Fact]
    public void Email_ShouldAcceptValidAddressAndRejectInvalidOne()
    {
        var policy = SystemParameterCatalog.Resolve("Email.FromAddress", SystemParameterType.String, "Email");

        SystemParameterValidator.TryNormalize(policy, SystemParameterType.String, "nao-responda@urbeat.com.br", out _, out var validError)
            .Should().BeTrue(validError);
        SystemParameterValidator.TryNormalize(policy, SystemParameterType.String, "sem-arroba", out _, out var invalidError)
            .Should().BeFalse();
        invalidError.Should().Contain("e-mail");
    }

    [Fact]
    public void Catalog_ShouldMarkHighImpactParametersAsRestricted()
    {
        SystemParameterCatalog.Resolve("Jwt.ExpirationMinutes", SystemParameterType.Int32, "Security")
            .Restricted.Should().BeTrue();
        SystemParameterCatalog.Resolve("Url.Asaas", SystemParameterType.String, "Url")
            .Restricted.Should().BeTrue();
        SystemParameterCatalog.Resolve("Email.SmtpPort", SystemParameterType.Int32, "Email")
            .Restricted.Should().BeTrue();
        SystemParameterCatalog.Resolve("Store.DefaultDeliveryFee", SystemParameterType.Decimal, "Business")
            .Restricted.Should().BeFalse();
    }

    [Fact]
    public void Catalog_ShouldMaskSecretKeysAndMakeThemReadOnly()
    {
        var policy = SystemParameterCatalog.Resolve("Payment.ApiSecret", SystemParameterType.String, "Payment");

        policy.Secret.Should().BeTrue();
        policy.ReadOnly.Should().BeTrue();
        policy.Restricted.Should().BeTrue();
        policy.Display.Should().Be(SystemParameterDisplayKind.Secret);
        SystemParameterCatalog.SecretMask.Should().NotContain("secret-value");
    }

    [Fact]
    public void Json_ShouldValidateAllowedExtensionsPattern()
    {
        var policy = SystemParameterCatalog.Resolve("Upload.AllowedExtensions", SystemParameterType.Json, "Upload");

        SystemParameterValidator.TryNormalize(policy, SystemParameterType.Json, ".jpg,.png", out _, out var validError)
            .Should().BeTrue(validError);
        SystemParameterValidator.TryNormalize(policy, SystemParameterType.Json, "jpg,png", out _, out var invalidError)
            .Should().BeFalse();
        invalidError.Should().Contain("extensões");
    }
}
