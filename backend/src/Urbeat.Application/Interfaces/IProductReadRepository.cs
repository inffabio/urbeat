using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IProductReadRepository
{
    Task<IReadOnlyCollection<ProductCategoryResponseDto>> ListCategoriesByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProductResponseDto>> ListAvailableProductsByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProductResponseDto>> ListFeaturedProductsByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default);
}
