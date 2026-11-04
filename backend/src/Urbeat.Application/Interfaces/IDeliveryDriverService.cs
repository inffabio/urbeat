using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Seller-side management of the operational motoboy link. The motoboy does not authenticate:
/// deliveries are operated exclusively through the public bearer links.
/// </summary>
public interface IDeliveryDriverService
{
    Task<IReadOnlyCollection<SellerDeliveryDriverResponseDto>> ListStoreDriversAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default);

    Task<SellerDeliveryDriverOperationResultDto> CreateStoreDriverAsync(
        Guid sellerUserId,
        UpsertStoreDeliveryDriverRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<SellerDeliveryDriverOperationResultDto> UpdateStoreDriverAsync(
        Guid sellerUserId,
        Guid storeDriverId,
        UpsertStoreDeliveryDriverRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<SellerDeliveryDriverOperationResultDto> SetStoreDriverActiveAsync(
        Guid sellerUserId,
        Guid storeDriverId,
        bool isActive,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the store link only when the driver has no order assignment or route history.
    /// Otherwise the caller must be told to deactivate the driver instead.
    /// </summary>
    Task<SellerDeliveryDriverOperationResultDto> DeleteStoreDriverAsync(
        Guid sellerUserId,
        Guid storeDriverId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AssignDeliveryDriverResultDto> AssignAsync(
        Guid sellerUserId,
        Guid orderId,
        Guid deliveryDriverId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AssignDeliveryDriverResultDto> RemoveAssignmentAsync(
        Guid sellerUserId,
        Guid orderId,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
