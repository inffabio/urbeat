namespace Urbeat.Domain.Entities;

public sealed class Order : BaseEntity
{
    public string Code { get; set; } = string.Empty;

    public Guid CustomerUserId { get; set; }

    public Guid StoreId { get; set; }

    public FulfillmentType FulfillmentType { get; set; }

    public Guid? CustomerAddressId { get; set; }

    public string? AddressCep { get; set; }

    public string? AddressStreet { get; set; }

    public string? AddressNumber { get; set; }

    public string? AddressNeighborhood { get; set; }

    public string? AddressCity { get; set; }

    public string? AddressState { get; set; }

    public string? AddressComplement { get; set; }

    public string? AddressReference { get; set; }

    /// <summary>
    /// Delivery destination coordinates captured at checkout from the customer address.
    /// They intentionally are a snapshot: editing the address later must not move an order
    /// that is already being delivered. Null means the address had no geocoded coordinates
    /// when the order was created.
    /// </summary>
    public double? DeliveryLatitude { get; set; }

    public double? DeliveryLongitude { get; set; }

    public string? Notes { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public OrderStatus Status { get; set; }

    /// <summary>
    /// Monotonic version of the order's lifecycle, advanced on every emitted status transition
    /// (creation is 1). Used as the per-order sequence in outbox events so a later status event is
    /// never delivered before an earlier one.
    /// </summary>
    public int StatusVersion { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal Total { get; set; }

    public DateTime? DeliveryConfirmedAtUtc { get; set; }

    public DateTime? SellerCompletedAtUtc { get; set; }

    /// <summary>
    /// Delivery PIN, stored as a PBKDF2 hash for authoritative validation. Only delivery
    /// orders have a PIN; pickup orders keep the existing confirmation flow.
    /// </summary>
    public string? DeliveryPinHash { get; set; }

    /// <summary>
    /// Encrypted copy of the delivery PIN so the customer tracking endpoint can show it.
    /// It is never returned to seller or driver responses.
    /// </summary>
    public string? DeliveryPinEncrypted { get; set; }

    public DateTime? DeliveryPinConfirmedAtUtc { get; set; }

    public Guid? DeliveryPinConfirmedByDriverId { get; set; }

    /// <summary>
    /// SHA-256 hash of the temporary public token used to share a single delivery with the
    /// motoboy without creating a route. The raw token is never persisted.
    /// </summary>
    public string? DeliveryLinkTokenHash { get; set; }

    /// <summary>
    /// Expiration of the individual delivery link. A null value means no link was shared.
    /// </summary>
    public DateTime? DeliveryLinkExpiresAtUtc { get; set; }
}
