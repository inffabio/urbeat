using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IPaymentWebhookService
{
    Task<ProcessWebhookResultDto> ProcessMercadoPagoWebhookAsync(
        string rawPayload,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
