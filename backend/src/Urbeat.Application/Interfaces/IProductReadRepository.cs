using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IProductReadRepository
{
    Task<IReadOnlyCollection<ProductCategoryResponseDto>> ListCategoriesByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProductResponseDto>> ListAvailableProductsByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compatibilidade do endpoint legado de "destaques": retorna os produtos com
    /// classificação especial <c>Offers</c> (Ofertas), sem removê-los do catálogo geral.
    /// </summary>
    Task<IReadOnlyCollection<ProductResponseDto>> ListFeaturedProductsByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default);
}
