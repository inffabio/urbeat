using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class UpsertStoreDeliveryDriverRequestDto
{
    public string Name { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public string? WhatsAppPhoneNumber { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class CreateDeliveryRouteRequestDto
{
    /// <summary>
    /// Store-scoped driver link identifier (StoreDeliveryDriver.Id). The server resolves and
    /// validates it against the authenticated seller's store.
    /// </summary>
    public Guid DeliveryDriverId { get; init; }

    /// <summary>Orders in the route sequence defined by the seller.</summary>
    public IReadOnlyCollection<Guid> OrderIds { get; init; } = [];
}

public sealed class UpdateDeliveryRouteRequestDto
{
    public Guid DeliveryDriverId { get; init; }

    public IReadOnlyCollection<Guid> OrderIds { get; init; } = [];
}

public sealed class DeliveryRouteOrderResponseDto
{
    public Guid OrderId { get; init; }

    public int Sequence { get; init; }

    public string Code { get; init; } = string.Empty;

    public string? CustomerName { get; init; }

    public string? AddressSummary { get; init; }

    public OrderStatus Status { get; init; }

    public DateTime? DeliveryConfirmedAtUtc { get; init; }

    public bool IsDelivered { get; init; }

    public bool CanDeliver { get; init; }
}

public sealed class DeliveryRouteResponseDto
{
    public Guid Id { get; init; }

    public Guid DeliveryDriverId { get; init; }

    public string DriverName { get; init; } = string.Empty;

    public string? DriverWhatsAppPhoneNumber { get; init; }

    public DeliveryRouteStatus Status { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public DateTime? RevokedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public int TotalStops { get; init; }

    public int DeliveredStops { get; init; }

    public IReadOnlyCollection<DeliveryRouteOrderResponseDto> Orders { get; init; } = [];
}

public sealed class DeliveryRouteOperationResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidState { get; init; }

    public bool DriverNotAvailable { get; init; }

    public bool OrderNotEligible { get; init; }

    public bool Conflict { get; init; }

    public string? Error { get; init; }

    public string? ShareUrl { get; init; }

    public DeliveryRouteResponseDto? Route { get; init; }
}

/// <summary>
/// Seller batch request to share every assigned delivery with the respective motoboy.
/// The server groups the selected orders by their authoritative assignment; the caller never
/// sends a driver id, so a driver from another store can never be targeted.
/// </summary>
public sealed class SendDeliveryRoutesRequestDto
{
    public IReadOnlyCollection<Guid> OrderIds { get; init; } = [];

    /// <summary>
    /// Operational declaration that the seller physically checked the selected volumes.
    /// This is not a security proof; the server still requires it to be explicitly true so a
    /// direct API call cannot skip the conference step by omitting the field.
    /// </summary>
    public bool PhysicalConfirmation { get; init; }
}

public sealed class SendDeliveryRouteGroupDto
{
    public Guid StoreDeliveryDriverId { get; init; }

    public Guid DeliveryDriverId { get; init; }

    public string DriverName { get; init; } = string.Empty;

    public string? WhatsAppPhoneNumber { get; init; }

    /// <summary>Absolute WhatsApp URL with the prepared message; null when no phone is configured.</summary>
    public string? WhatsAppUrl { get; init; }

    /// <summary>Absolute public route URL. Only this link is shared with the motoboy.</summary>
    public string ShareUrl { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    /// <summary>Orders newly included in this batch (excludes orders already in the route).</summary>
    public int OrderCount { get; init; }

    /// <summary>Total stops in the public link, including orders already shared before.</summary>
    public int TotalStops { get; init; }

    public DeliveryRouteResponseDto Route { get; init; } = new();
}

public sealed class SendDeliveryRoutesResultDto
{
    public bool Forbidden { get; init; }

    public bool PhysicalConfirmationRequired { get; init; }

    public bool OrderNotEligible { get; init; }

    public bool DriverNotAvailable { get; init; }

    public bool Conflict { get; init; }

    public string? Error { get; init; }

    public IReadOnlyCollection<SendDeliveryRouteGroupDto> Groups { get; init; } = [];
}

public sealed class ShareDeliveryRouteResultDto
{
    public string ShareUrl { get; init; } = string.Empty;

    public DeliveryRouteResponseDto Route { get; init; } = new();
}

/// <summary>
/// Result of sharing a single delivery link. The raw token is only present in <see cref="ShareUrl"/>;
/// it is never persisted and never echoed by public endpoints.
/// </summary>
public sealed class ShareIndividualDeliveryResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidState { get; init; }

    public string? Error { get; init; }

    public string? ShareUrl { get; init; }
}

public sealed class PublicDeliveryRouteStopDto
{
    /// <summary>Opaque positional identifier; the order id is never exposed publicly.</summary>
    public int Sequence { get; init; }

    public string OrderCode { get; init; } = string.Empty;

    public OrderStatus Status { get; init; }

    public bool IsNext { get; init; }

    public bool CanDeliver { get; init; }

    public string? CustomerName { get; init; }

    public string? CustomerPhoneNumber { get; init; }

    public string? AddressCep { get; init; }

    public string? AddressStreet { get; init; }

    public string? AddressNumber { get; init; }

    public string? AddressNeighborhood { get; init; }

    public string? AddressCity { get; init; }

    public string? AddressState { get; init; }

    public string? AddressComplement { get; init; }

    public string? AddressReference { get; init; }

    public string? AddressSummary { get; init; }

    public double? DestinationLatitude { get; init; }

    public double? DestinationLongitude { get; init; }

    public string? Notes { get; init; }

    public DateTime? DeliveryConfirmedAtUtc { get; init; }
}

public sealed class PublicDeliveryRouteResponseDto
{
    public string StoreName { get; init; } = string.Empty;

    public string DriverName { get; init; } = string.Empty;

    public DeliveryRouteStatus Status { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public double? OriginLatitude { get; init; }

    public double? OriginLongitude { get; init; }

    public int? NextSequence { get; init; }

    public int TotalStops { get; init; }

    public int DeliveredStops { get; init; }

    public IReadOnlyCollection<PublicDeliveryRouteStopDto> Stops { get; init; } = [];
}

public sealed class PublicDeliveryRouteAccessResultDto
{
    public bool NotFound { get; init; }

    public bool Gone { get; init; }

    public PublicDeliveryRouteResponseDto? Route { get; init; }
}

public sealed class PublicDeliveryRoutePinResultDto
{
    public bool Succeeded { get; init; }

    public bool NotFound { get; init; }

    public bool Gone { get; init; }

    public bool InvalidState { get; init; }

    public bool PinInvalid { get; init; }

    public bool PinLocked { get; init; }

    public bool PinUnavailable { get; init; }

    public bool ConcurrentUpdate { get; init; }

    public int RemainingAttempts { get; init; }

    public OrderStatus? OrderStatus { get; init; }

    public DateTime? DeliveryConfirmedAtUtc { get; init; }

    public PublicDeliveryRouteResponseDto? Route { get; init; }
}
