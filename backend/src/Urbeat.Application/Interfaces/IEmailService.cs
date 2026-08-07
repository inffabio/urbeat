namespace Urbeat.Application.Interfaces;

public interface IEmailService
{
    Task SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}
