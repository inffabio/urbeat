using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IProductService
{
    Task<IReadOnlyCollection<ProductResponseDto>> ListByStoreAsync(
        Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default);

    Task<UpdateProductResultDto> CreateAsync(
        Guid ownerUserId, Guid storeId, CreateProductRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default);

    Task<UpdateProductResultDto> UpdateAsync(
        Guid ownerUserId, Guid productId, UpdateProductRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default);

    Task<UpdateProductResultDto> UpdateAvailabilityAsync(
        Guid ownerUserId, Guid productId, bool isAvailable,
        string? ipAddress, CancellationToken cancellationToken = default);

    Task<UpdateProductResultDto> UpdateImageAsync(
        Guid ownerUserId, Guid productId, string imageUrl,
        string? ipAddress, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid ownerUserId, Guid productId,
        string? ipAddress, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProductResponseDto>> BatchUpsertAsync(
        Guid ownerUserId, Guid storeId, BatchUpsertProductsRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default);
}
