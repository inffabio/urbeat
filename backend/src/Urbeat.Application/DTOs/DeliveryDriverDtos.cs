using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

/// <summary>
/// Seller-side view of the operational motoboy link. The motoboy has no account, e-mail or
/// password; the store owns registration, activation and order assignment.
/// </summary>
public sealed class SellerDeliveryDriverResponseDto
{
    /// <summary>Store-scoped link id used by seller assignment and route endpoints.</summary>
    public Guid Id { get; init; }

    /// <summary>Global driver id used by order assignment summaries.</summary>
    public Guid DeliveryDriverId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public string? WhatsAppPhoneNumber { get; init; }

    public bool IsActive { get; init; }

    public StoreDeliveryDriverStatus Status { get; init; }

    public DateTime? AcceptedAtUtc { get; init; }

    public DateTime? RevokedAtUtc { get; init; }
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

/// <summary>
/// PIN confirmation accepted only by the public bearer links. There is no authenticated driver,
/// so the motoboy must explicitly confirm the handover/payment in the same request whenever the
/// order requires it. The server validates and persists both together with a valid PIN.
/// </summary>
public sealed class ConfirmDeliveryPinRequestDto
{
    public string Pin { get; init; } = string.Empty;

    /// <summary>
    /// Explicit manual confirmation of the handover/payment on the public link. Required (and
    /// never persisted without a valid PIN) for orders under the current pay-on-delivery contract.
    /// </summary>
    public bool HandoverConfirmed { get; init; }

    /// <summary>
    /// Cash amount actually received, informed by the motoboy on the public link. Validated and
    /// converted into change by the server; the client never sends the change value.
    /// </summary>
    public decimal? CashReceivedAmount { get; init; }
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
/// Compatibility payload for POST /api/delivery-drivers/store. Only the operational motoboy
/// fields are accepted; e-mail/invite data was removed with the authenticated driver model.
/// </summary>
public sealed class CreateStoreDeliveryDriverRequestDto
{
    public string? Name { get; init; }

    public string? FullName { get; init; }

    public string? PhoneNumber { get; init; }

    public string? WhatsAppPhoneNumber { get; init; }

    public bool IsActive { get; init; } = true;
}
