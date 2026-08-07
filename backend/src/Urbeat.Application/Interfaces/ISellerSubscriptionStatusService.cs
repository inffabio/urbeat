using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ISellerSubscriptionStatusService
{
    Task<ContractSellerSubscriptionResultDto> ContractAsync(
        Guid sellerUserId,
        ContractSellerSubscriptionRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(UpsertSellerSubscriptionStatusRequestDto request, CancellationToken cancellationToken = default);

    Task<SellerSubscriptionMyResponseDto> GetMySubscriptionAsync(Guid sellerUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SellerSubscriptionChargeHistoryItemDto>> ListMyChargeHistoryAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default);
}
