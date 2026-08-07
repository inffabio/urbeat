using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IReviewService
{
    Task<ReviewResponseDto> CreateOrUpdateAsync(
        Guid customerUserId,
        Guid orderId,
        CreateReviewRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ReviewResponseDto?> GetByOrderAsync(
        Guid customerUserId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreReviewResponseDto>> ListByStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreReviewResponseDto>> ListBySellerAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default);
}
