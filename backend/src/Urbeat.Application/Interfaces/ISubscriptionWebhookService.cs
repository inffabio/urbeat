using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ISubscriptionWebhookService
{
    Task<ProcessWebhookResultDto> ProcessAsaasWebhookAsync(
        string rawPayload,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}