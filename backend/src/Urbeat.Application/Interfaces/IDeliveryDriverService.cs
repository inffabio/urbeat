using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

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

    /// <summary>Legacy invite flow, kept for compatibility with links already sent.</summary>
    Task<SellerDeliveryDriverOperationResultDto> InviteAsync(
        Guid sellerUserId,
        InviteDeliveryDriverRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<SellerDeliveryDriverOperationResultDto> ResendInviteAsync(
        Guid sellerUserId,
        Guid storeDriverId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<SellerDeliveryDriverOperationResultDto> RevokeAsync(
        Guid sellerUserId,
        Guid storeDriverId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<DeliveryDriverInvitePreviewResponseDto> GetInvitePreviewAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<AcceptDeliveryDriverInviteResultDto> AcceptInviteAsync(
        string token,
        AcceptDeliveryDriverInviteRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DeliveryDriverStoreResponseDto>> ListDriverStoresAsync(
        Guid driverUserId,
        CancellationToken cancellationToken = default);

    Task<DriverOrdersResultDto> ListDriverOrdersAsync(
        Guid driverUserId,
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<DriverOrderSummaryResponseDto?> GetDriverOrderAsync(
        Guid driverUserId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<DeliveryDriverActionResultDto> StartDeliveryAsync(
        Guid driverUserId,
        Guid orderId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<DeliveryDriverActionResultDto> ConfirmDeliveryPinAsync(
        Guid driverUserId,
        Guid orderId,
        string pin,
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
