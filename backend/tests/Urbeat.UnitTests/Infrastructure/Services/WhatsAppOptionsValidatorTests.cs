using FluentAssertions;
using Urbeat.Infrastructure.Services.WhatsApp;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class WhatsAppOptionsValidatorTests
{
    [Fact]
    public void Validate_WithoutWebhookEnabledAndWithoutSecrets_ShouldSucceed()
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WebhookEnabledWithoutSecrets_ShouldFailWithoutEchoingValues()
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions { WebhookEnabled = true });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("WebhookVerifyToken");
        result.FailureMessage.Should().Contain("AppSecret");
        result.FailureMessage.Should().Contain("PhoneNumberId");
    }

    [Fact]
    public void Validate_WebhookEnabledWithSecrets_ShouldSucceed()
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions
        {
            WebhookEnabled = true,
            WebhookVerifyToken = "configured-token",
            AppSecret = "configured-secret",
            PhoneNumberId = "1436364406224525"
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultGraphApiVersion_ShouldBeSupportedAndValid()
    {
        var validator = new WhatsAppOptionsValidator();

        WhatsAppOptions.DefaultGraphApiVersion.Should().Be("v26.0");
        validator.Validate(null, new WhatsAppOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("21.0")]
    [InlineData("v21")]
    [InlineData("v21.0.1")]
    public void Validate_InvalidGraphApiVersion_ShouldFail(string version)
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions { GraphApiVersion = version });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GraphApiVersion");
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("1436 3644")]
    [InlineData("-1")]
    public void Validate_InvalidPhoneNumberId_ShouldFail(string phoneNumberId)
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions { PhoneNumberId = phoneNumberId });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PhoneNumberId");
    }

    [Theory]
    [InlineData("http://graph.facebook.com")]
    [InlineData("not-a-url")]
    public void Validate_InvalidGraphApiBaseUrl_ShouldFail(string baseUrl)
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions { GraphApiBaseUrl = baseUrl });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GraphApiBaseUrl");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123 456")]
    [InlineData("-1")]
    public void Validate_InvalidMetaAppId_ShouldFail(string metaAppId)
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions { MetaAppId = metaAppId });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MetaAppId");
    }

    [Theory]
    [InlineData("http://urbeat.com.br/assets/images/urbeat-logo.jpg")]
    [InlineData("https://localhost/assets/images/urbeat-logo.jpg")]
    [InlineData("https://user:secret@urbeat.com.br/assets/images/urbeat-logo.jpg")]
    public void Validate_InvalidProfilePictureUrl_ShouldFail(string profilePictureUrl)
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions { ProfilePictureUrl = profilePictureUrl });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ProfilePictureUrl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Validate_InvalidTimeout_ShouldFail(int timeoutSeconds)
    {
        var validator = new WhatsAppOptionsValidator();

        var result = validator.Validate(null, new WhatsAppOptions { TimeoutSeconds = timeoutSeconds });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TimeoutSeconds");
    }
}
