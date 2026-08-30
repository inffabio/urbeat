using FluentAssertions;
using Urbeat.Infrastructure.Outbox;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class OutboxErrorSanitizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sanitize_ShouldReturnEmpty_ForNullOrBlank(string? error)
    {
        OutboxErrorSanitizer.Sanitize(error).Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_ShouldRedactEmailAddresses()
    {
        OutboxErrorSanitizer.Sanitize("smtp rejected user@urbeat.local")
            .Should().NotContain("user@urbeat.local")
            .And.Contain("[email]");
    }

    [Fact]
    public void Sanitize_ShouldRedactUrls()
    {
        OutboxErrorSanitizer.Sanitize("failed https://app.urbeat.test/c/ABC123XYZ")
            .Should().NotContain("ABC123XYZ")
            .And.Contain("[url]");
    }

    [Fact]
    public void Sanitize_ShouldRedactPhoneNumbers()
    {
        OutboxErrorSanitizer.Sanitize("sms to +5511999998888 rejected")
            .Should().NotContain("5511999998888")
            .And.Contain("[phone]");
    }

    [Fact]
    public void Sanitize_ShouldRedactCredentialFragments()
    {
        OutboxErrorSanitizer.Sanitize("call failed api_key=abc123 token=xyz789")
            .Should().NotContain("abc123")
            .And.NotContain("xyz789")
            .And.Contain("[redacted]");
    }

    [Fact]
    public void Sanitize_ShouldRedactBearerTokens()
    {
        OutboxErrorSanitizer.Sanitize("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.secret-part")
            .Should().NotContain("eyJhbGciOiJIUzI1NiJ9")
            .And.NotContain("secret-part")
            .And.Contain("[redacted]");
    }

    [Fact]
    public void Sanitize_ShouldRedactColonSeparatedTokens()
    {
        OutboxErrorSanitizer.Sanitize("rejected token: abc123xyz")
            .Should().NotContain("abc123xyz")
            .And.Contain("[redacted]");
    }

    [Fact]
    public void Sanitize_ShouldRedactJsonSecrets()
    {
        OutboxErrorSanitizer.Sanitize("""failed {"access_token":"abc123","password":"s3cret"}""")
            .Should().NotContain("abc123")
            .And.NotContain("s3cret")
            .And.Contain("[redacted]");
    }

    [Fact]
    public void Sanitize_ShouldRedactCommonPasswordAndKeyPatterns()
    {
        OutboxErrorSanitizer.Sanitize("auth failed password=hunter2 client_secret=shh pwd=opensesame")
            .Should().NotContain("hunter2")
            .And.NotContain("shh")
            .And.NotContain("opensesame")
            .And.Contain("[redacted]");
    }

    [Fact]
    public void Sanitize_ShouldTruncateToMaxLength()
    {
        var longError = new string('a', 1000);

        OutboxErrorSanitizer.Sanitize(longError).Length.Should().Be(OutboxErrorSanitizer.MaxLength);
    }

    [Fact]
    public void Sanitize_ShouldPrefixCategory_WhenProvided()
    {
        OutboxErrorSanitizer.Sanitize("boom", "EmailHandler")
            .Should().StartWith("EmailHandler:");
    }

    [Fact]
    public void Sanitize_ShouldPreserveSafeDiagnosticFragments()
    {
        OutboxErrorSanitizer.Sanitize("SMTP unavailable")
            .Should().Contain("SMTP unavailable");
    }
}
