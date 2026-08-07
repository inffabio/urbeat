using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IProductCategoryService
{
    Task<IReadOnlyCollection<ProductCategoryResponseDto>> ListByStoreAsync(
        Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default);

    Task<UpsertProductCategoryResultDto> CreateAsync(
        Guid ownerUserId, Guid storeId, CreateProductCategoryRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default);

    Task<UpsertProductCategoryResultDto> UpdateAsync(
        Guid ownerUserId, Guid categoryId, UpdateProductCategoryRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default);

    Task<ProductCategoryDeleteResult> DeleteAsync(
        Guid ownerUserId, Guid categoryId,
        Guid? reassignCategoryId = null,
        string? ipAddress = null, CancellationToken cancellationToken = default);

    Task<ReorderStoreCategoriesResult> ReorderAsync(
        Guid ownerUserId, Guid storeId,
        ReorderStoreCategoriesRequestDto items,
        string? ipAddress, CancellationToken cancellationToken = default);
}
