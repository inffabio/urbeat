using Urbeat.Application.DTOs.Publish;

namespace Urbeat.Application.Interfaces.Publish;

public interface IStorePublishService
{
    Task<StorePublishSummaryDto> GetStorePublishSummaryAsync(Guid storeId, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> PublishStoreAsync(Guid storeId, Guid ownerId, CancellationToken cancellationToken);
}
