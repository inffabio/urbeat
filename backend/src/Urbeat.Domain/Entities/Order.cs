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
}
