using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class InviteDeliveryDriverRequestDto
{
    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;
}

public sealed class SellerDeliveryDriverResponseDto
{
    /// <summary>Store-scoped link id used by seller assignment and route endpoints.</summary>
    public Guid Id { get; init; }

    /// <summary>Global driver id used by order assignment summaries.</summary>
    public Guid DeliveryDriverId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public string? WhatsAppPhoneNumber { get; init; }

    public bool IsActive { get; init; }

    public bool HasAccount { get; init; }

    public StoreDeliveryDriverStatus Status { get; init; }

    public DateTime? InviteSentAtUtc { get; init; }

    public DateTime? InviteExpiresAtUtc { get; init; }

    public DateTime? AcceptedAtUtc { get; init; }

    public DateTime? RevokedAtUtc { get; init; }

    public bool CanResendInvite { get; init; }
}

public sealed class DeliveryDriverInvitePreviewResponseDto
{
    public bool Valid { get; init; }

    public bool AlreadyAccepted { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string DriverName { get; init; } = string.Empty;

    public string MaskedEmail { get; init; } = string.Empty;

    public DateTime? ExpiresAtUtc { get; init; }
}

public sealed class AcceptDeliveryDriverInviteRequestDto
{
    public string Password { get; init; } = string.Empty;

    public string? FullName { get; init; }
}

public sealed class AcceptDeliveryDriverInviteResultDto
{
    public bool Succeeded { get; init; }

    public bool InvalidInvite { get; init; }

    public bool InvalidPassword { get; init; }

    public string? StoreName { get; init; }

    public string? Error { get; init; }
}

public sealed class DeliveryDriverStoreResponseDto
{
    public Guid StoreId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public DateTime? AcceptedAtUtc { get; init; }

    public int AssignedOrderCount { get; init; }
}

public sealed class DriverOrderSummaryResponseDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public Guid StoreId { get; init; }

    public OrderStatus Status { get; init; }

    public FulfillmentType FulfillmentType { get; init; }

    public PaymentMethod PaymentMethod { get; init; }

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

    /// <summary>
    /// Persisted store address coordinates (origin). Null when the store has no coordinates;
    /// the driver portal must fall back to the address text and external navigation.
    /// </summary>
    public double? OriginLatitude { get; init; }

    public double? OriginLongitude { get; init; }

    /// <summary>
    /// Delivery coordinates captured in the order at checkout (destination). Reading the order
    /// snapshot means editing the customer address later cannot move an order already in route.
    /// Null when the address had no geocoded coordinates at checkout.
    /// </summary>
    public double? DestinationLatitude { get; init; }

    public double? DestinationLongitude { get; init; }

    public string? Notes { get; init; }

    public decimal Subtotal { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal Total { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? DeliveryStartedAtUtc { get; init; }

    public DateTime? DeliveryConfirmedAtUtc { get; init; }

    public IReadOnlyCollection<OrderItemResponseDto> Items { get; init; } = [];

    public bool CanStartDelivery { get; init; }

    public bool CanConfirmPin { get; init; }
}

public sealed class DriverOrdersResultDto
{
    public bool StoreNotAvailable { get; init; }

    public IReadOnlyCollection<DriverOrderSummaryResponseDto> Orders { get; init; } = [];
}

public sealed class AssignDeliveryDriverRequestDto
{
    /// <summary>
    /// Store-scoped driver link identifier (StoreDeliveryDriver.Id), not the global driver id.
    /// The server resolves and validates the link against the authenticated seller's store.
    /// </summary>
    public Guid DeliveryDriverId { get; init; }
}

public sealed class AssignDeliveryDriverResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidState { get; init; }

    public bool DriverNotAvailable { get; init; }

    public OrderDetailsResponseDto? Order { get; init; }
}

public sealed class ConfirmDeliveryPinRequestDto
{
    public string Pin { get; init; } = string.Empty;
}

public sealed class DeliveryDriverActionResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidState { get; init; }

    public bool PinInvalid { get; init; }

    public bool PinLocked { get; init; }

    public bool PinUnavailable { get; init; }

    /// <summary>
    /// A concurrent PIN request won the optimistic concurrency check. The client must refresh
    /// the order state before retrying; the final attempt count never exceeds the limit.
    /// </summary>
    public bool ConcurrentUpdate { get; init; }

    public int RemainingAttempts { get; init; }

    public OrderStatus? Status { get; init; }

    public DateTime? DeliveryConfirmedAtUtc { get; init; }
}

public sealed class SellerDeliveryDriverOperationResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool Conflict { get; init; }

    public string? Error { get; init; }

    public SellerDeliveryDriverResponseDto? Driver { get; init; }
}

public sealed class SetStoreDeliveryDriverStatusRequestDto
{
    public bool IsActive { get; init; }
}

/// <summary>
/// Compatibility payload for POST /api/delivery-drivers/store. New clients send the simple
/// motoboy fields; clients of the removed invite UI still send FullName/Email and are routed
/// to the legacy invite flow instead of being silently ignored.
/// </summary>
public sealed class CreateStoreDeliveryDriverRequestDto
{
    public string? Name { get; init; }

    public string? FullName { get; init; }

    public string? Email { get; init; }

    public string? PhoneNumber { get; init; }

    public string? WhatsAppPhoneNumber { get; init; }

    public bool IsActive { get; init; } = true;
}
