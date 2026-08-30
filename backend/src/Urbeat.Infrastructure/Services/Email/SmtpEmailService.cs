using Urbeat.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Urbeat.Infrastructure.Services.Email;

public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        string? textBody = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (_options.LogOnly || string.IsNullOrWhiteSpace(_options.Smtp.Host))
        {
            _logger.LogInformation(
                "{EventType} | Email skipped (LogOnly or no SMTP host) | To={To} | Subject={Subject}",
                "EMAIL_LOG_ONLY", toAddress, subject);
            return null;
        }

        var message = new MimeMessage();
        // SMTP offers no server-side idempotency; the client-generated Message-Id is the best
        // available correlation identifier for diagnostics and provider-side dedup at the MTA.
        message.MessageId = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"{Guid.CreateVersion7()}@urbeat"
            : $"{idempotencyKey}@urbeat";
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = textBody ?? StripHtml(htmlBody),
        };
        message.Body = bodyBuilder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            // Port 465 requires implicit SSL (SslOnConnect). Port 587 typically uses StartTls.
            var socketOptions = _options.Smtp.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : (_options.Smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);

            await client.ConnectAsync(_options.Smtp.Host, _options.Smtp.Port, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
            {
                await client.AuthenticateAsync(_options.Smtp.Username, _options.Smtp.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation(
                "{EventType} | Email sent | To={To} | Subject={Subject}",
                "EMAIL_SENT", toAddress, subject);

            return message.MessageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "{EventType} | Email failed | To={To} | Subject={Subject}",
                "EMAIL_FAILED", toAddress, subject);
            throw;
        }
    }

    private static string StripHtml(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
    }
}
