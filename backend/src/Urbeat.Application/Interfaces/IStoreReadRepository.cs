using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IStoreReadRepository
{
    Task<StoreResponseDto?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StorePublicListItemDto>> ListPublicAsync(string? cuisineType, CancellationToken cancellationToken = default);

    Task<StorePublicDetailsDto?> GetPublicByIdAsync(Guid storeId, CancellationToken cancellationToken = default);

    Task<StorePublicDetailsDto?> GetPublicBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<StorePublicDetailsDto?> GetPublicByPathAsync(string storePath, CancellationToken cancellationToken = default);
}