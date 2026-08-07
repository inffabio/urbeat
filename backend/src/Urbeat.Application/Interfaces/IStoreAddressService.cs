using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IStoreAddressService
{
    Task<StoreAddressResponseDto?> GetByStoreAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default);

    Task<UpsertStoreAddressResultDto> UpsertAsync(
        Guid ownerUserId,
        Guid storeId,
        UpdateStoreAddressRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}