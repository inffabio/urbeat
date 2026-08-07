using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IStoreService
{
    Task<(bool Created, bool AlreadyExists, bool InvalidCuisineType, StoreResponseDto? Store)> CreateForOwnerAsync(
        Guid ownerUserId,
        CreateStoreRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<StoreResponseDto?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);

    Task<UpdateStoreResultDto> UpdateAsync(
        Guid ownerUserId,
        Guid storeId,
        UpdateStoreRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<UpdateStoreResultDto> UpdateStatusAsync(
        Guid ownerUserId,
        Guid storeId,
        bool isOpen,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<UpdateStoreResultDto> UpdateDeliveryConfigAsync(
        Guid ownerUserId,
        Guid storeId,
        decimal deliveryFee,
        decimal minimumOrderValue,
        decimal? freeShippingThreshold,
        bool freeShippingToday,
        IEnumerable<StoreDeliveryAreaDto>? deliveryAreas,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DeliveryTimeResponseDto>> GetActiveDeliveryTimesAsync(Guid storeId, CancellationToken cancellationToken = default);

    Task<DeliveryTimeResponseDto?> CreateDeliveryTimeAsync(Guid storeId, int minTimeMinutes, int maxTimeMinutes, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>> GetActiveDeliveryNeighborhoodsAsync(string city, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>> GetActiveDeliveryNeighborhoodsByStoreAsync(Guid storeId, CancellationToken cancellationToken = default);

    Task<DeliveryNeighborhoodResponseDto?> CreateDeliveryNeighborhoodAsync(string neighborhood, string city, CancellationToken cancellationToken = default);
}
