using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Seller-authenticated delivery route management and the public tokenized driver views.
/// Every seller method resolves the store from the authenticated owner; public methods only
/// accept the opaque route token and never trust a store/driver id from the caller.
/// </summary>
public interface IDeliveryRouteService
{
    Task<IReadOnlyCollection<DeliveryRouteResponseDto>> ListStoreRoutesAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default);

    Task<DeliveryRouteOperationResultDto> GetStoreRouteAsync(
        Guid sellerUserId,
        Guid routeId,
        CancellationToken cancellationToken = default);

    Task<DeliveryRouteOperationResultDto> CreateAsync(
        Guid sellerUserId,
        CreateDeliveryRouteRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<DeliveryRouteOperationResultDto> UpdateAsync(
        Guid sellerUserId,
        Guid routeId,
        UpdateDeliveryRouteRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<DeliveryRouteOperationResultDto> RevokeAsync(
        Guid sellerUserId,
        Guid routeId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<DeliveryRouteOperationResultDto> ShareAsync(
        Guid sellerUserId,
        Guid routeId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<PublicDeliveryRouteAccessResultDto> GetPublicRouteAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<PublicDeliveryRoutePinResultDto> ConfirmPublicDeliveryAsync(
        string token,
        int sequence,
        string pin,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
