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

    public decimal Subtotal { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal Total { get; set; }
}
