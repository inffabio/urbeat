namespace Urbeat.Application.Interfaces;

public interface IEmailService
{
    /// <summary>
    /// Sends an e-mail. Returns the provider/message identifier when one is available, otherwise
    /// <see langword="null"/>. <paramref name="idempotencyKey"/> is forwarded to providers that
    /// support server-side deduplication (e.g. Infobip); SMTP has no such guarantee and only uses it
    /// as the outbound <c>Message-Id</c> for correlation. Delivery is at-least-once.
    /// </summary>
    Task<string?> SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        string? textBody = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}
