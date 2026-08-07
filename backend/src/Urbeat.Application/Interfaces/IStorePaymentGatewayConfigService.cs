using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Interfaces;

public interface IStorePaymentGatewayConfigService
{
    Task<PaymentGatewayConfigResponseDto?> GetByStoreAsync(
        Guid ownerUserId, Guid storeId, PaymentGateway gateway, CancellationToken cancellationToken = default);

    Task<UpsertPaymentGatewayConfigResultDto> UpsertAsync(
        Guid ownerUserId, Guid storeId, UpsertPaymentGatewayConfigRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid ownerUserId, Guid storeId, PaymentGateway gateway,
        string? ipAddress, CancellationToken cancellationToken = default);
}
