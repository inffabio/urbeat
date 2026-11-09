using FluentAssertions;
using Urbeat.Infrastructure.Services.WhatsApp;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class WhatsAppWebhookSignatureTests
{
    private const string AppSecret = "unit-test-app-secret";
    private const string Payload = "{\"object\":\"whatsapp_business_account\"}";

    [Fact]
    public void IsValidSignature_WithExactPayload_ReturnsTrue()
    {
        var signature = WhatsAppWebhookSignature.ComputeSignature(AppSecret, System.Text.Encoding.UTF8.GetBytes(Payload));

        WhatsAppWebhookSignature.IsValidSignature(AppSecret, Payload, signature).Should().BeTrue();
    }

    [Fact]
    public void IsValidSignature_WhenPayloadChanges_ReturnsFalse()
    {
        var signature = WhatsAppWebhookSignature.ComputeSignature(AppSecret, System.Text.Encoding.UTF8.GetBytes(Payload));

        WhatsAppWebhookSignature.IsValidSignature(AppSecret, Payload + " ", signature).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=abc")]
    [InlineData("sha256=abc")]
    [InlineData("sha256=zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void IsValidSignature_WithMissingOrMalformedHeader_ReturnsFalse(string? signature)
    {
        WhatsAppWebhookSignature.IsValidSignature(AppSecret, Payload, signature).Should().BeFalse();
    }

    [Fact]
    public void IsValidSignature_WithoutConfiguredSecret_ReturnsFalse()
    {
        var signature = WhatsAppWebhookSignature.ComputeSignature(AppSecret, System.Text.Encoding.UTF8.GetBytes(Payload));

        WhatsAppWebhookSignature.IsValidSignature(string.Empty, Payload, signature).Should().BeFalse();
    }

    [Fact]
    public void TokensMatch_ShouldCompareTokensWithoutLeakingThem()
    {
        WhatsAppWebhookSignature.TokensMatch("expected-token", "expected-token").Should().BeTrue();
        WhatsAppWebhookSignature.TokensMatch("expected-token", "other-token").Should().BeFalse();
        WhatsAppWebhookSignature.TokensMatch("expected-token", null).Should().BeFalse();
        WhatsAppWebhookSignature.TokensMatch(null, "expected-token").Should().BeFalse();
        WhatsAppWebhookSignature.TokensMatch("", "").Should().BeFalse();
    }
}
