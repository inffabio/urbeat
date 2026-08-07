using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IStoreBusinessHoursService
{
    Task<StoreBusinessHoursResponseDto?> GetAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default);

    Task<(bool NotFound, bool Forbidden, StoreBusinessHoursResponseDto? Hours)> UpsertAsync(
        Guid ownerUserId,
        Guid storeId,
        UpsertStoreBusinessHoursRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}