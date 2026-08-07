using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IStoreAdditionalService
{
    Task<IReadOnlyCollection<StoreAdditionalDto>> ListAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<StoreAdditionalGroupDto>> ListGroupsAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default);
    Task<(StoreAdditionalDto? Additional, bool NotFound, bool Forbidden)> CreateAsync(Guid ownerUserId, Guid storeId, StoreAdditionalRequestDto request, CancellationToken cancellationToken = default);
    Task<(StoreAdditionalDto? Additional, bool NotFound, bool Forbidden)> UpdateAsync(Guid ownerUserId, Guid storeId, Guid additionalId, StoreAdditionalRequestDto request, CancellationToken cancellationToken = default);
    Task<(StoreAdditionalDto? Additional, bool NotFound, bool Forbidden)> UpdateStatusAsync(Guid ownerUserId, Guid storeId, Guid additionalId, bool isActive, CancellationToken cancellationToken = default);
    Task<StoreAdditionalDeleteResult> DeleteAsync(Guid ownerUserId, Guid storeId, Guid additionalId, CancellationToken cancellationToken = default);
}
