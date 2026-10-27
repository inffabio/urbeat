namespace Urbeat.Domain.Entities;

/// <summary>
/// Store-scoped link between a store and its motoboy contact record.
/// Only one link may exist per (store, driver). New records are created directly as
/// <see cref="StoreDeliveryDriverStatus.Active"/> or <see cref="StoreDeliveryDriverStatus.Revoked"/>;
/// the pending/invite lifecycle is kept only for links created by the legacy invite flow.
/// The raw invite token is never persisted — only its SHA-256 hash.
/// </summary>
public sealed class StoreDeliveryDriver : BaseEntity
{
    public Guid StoreId { get; set; }

    public Guid DeliveryDriverId { get; set; }

    public DeliveryDriver? DeliveryDriver { get; set; }

    public StoreDeliveryDriverStatus Status { get; set; } = StoreDeliveryDriverStatus.Pending;

    public string DisplayName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>WhatsApp number used by the seller grid. Null uses the main phone number.</summary>
    public string? WhatsAppPhoneNumber { get; set; }

    public string? InviteTokenHash { get; set; }

    public DateTime? InviteExpiresAtUtc { get; set; }

    public DateTime? InviteSentAtUtc { get; set; }

    /// <summary>Incremented on every resend so delivery keys stay unique per invite e-mail.</summary>
    public int InviteVersion { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Guid? InvitedByUserId { get; set; }
}
