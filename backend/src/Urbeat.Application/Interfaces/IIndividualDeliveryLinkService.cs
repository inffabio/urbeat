using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Single-delivery sharing used when the store does not want to create a route. The seller side
/// is authenticated and scoped to the store that owns the order; the public side only accepts the
/// opaque token (stored as a hash) and never exposes the delivery PIN.
/// </summary>
public interface IIndividualDeliveryLinkService
{
    Task<ShareIndividualDeliveryResultDto> ShareAsync(
        Guid sellerUserId,
        Guid orderId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<PublicDeliveryRouteAccessResultDto> GetPublicDeliveryAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<PublicDeliveryRoutePinResultDto> ConfirmPublicDeliveryAsync(
        string token,
        string pin,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
