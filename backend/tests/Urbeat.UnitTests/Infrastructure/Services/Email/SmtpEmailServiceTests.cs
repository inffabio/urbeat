using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.UnitTests.Infrastructure.Services.Email;

/// <summary>
/// The SMTP transport must never report a delivery it did not perform. Development can opt into
/// the log-only mode explicitly; a send request with no host configured is a misconfiguration and
/// must fail instead of returning success, otherwise the caller records the e-mail as sent.
/// </summary>
public sealed class SmtpEmailServiceTests
{
    [Fact]
    public async Task SendAsync_ShouldSkipOnlyWhenLogOnlyIsExplicitlyEnabled()
    {
        var service = CreateService(new EmailOptions
        {
            LogOnly = true,
            Smtp = new SmtpOptions { Host = string.Empty }
        });

        var messageId = await service.SendAsync(
            "destinatario@exemplo.com",
            "Destinatário",
            "Assunto",
            "<p>Corpo</p>");

        messageId.Should().BeNull("the log-only transport does not produce a message id");
    }

    [Fact]
    public async Task SendAsync_ShouldFailClosed_WhenTheSmtpHostIsMissing()
    {
        var service = CreateService(new EmailOptions
        {
            LogOnly = false,
            Smtp = new SmtpOptions { Host = string.Empty }
        });

        var send = () => service.SendAsync(
            "destinatario@exemplo.com",
            "Destinatário",
            "Assunto",
            "<p>Corpo</p>");

        await send.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Smtp:Host*");
    }

    private static SmtpEmailService CreateService(EmailOptions options)
        => new(
            Options.Create(options),
            NullLogger<SmtpEmailService>.Instance);
}
