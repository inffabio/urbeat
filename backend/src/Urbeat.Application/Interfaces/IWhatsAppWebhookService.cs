using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IWhatsAppWebhookService
{
    Task<ProcessWhatsAppWebhookResultDto> ProcessAsync(
        string rawPayload,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
