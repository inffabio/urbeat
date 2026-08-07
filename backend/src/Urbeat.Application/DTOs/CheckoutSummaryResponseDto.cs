using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class CheckoutSummaryResponseDto
{
    public Guid StoreId { get; init; }

    public FulfillmentType FulfillmentType { get; init; }

    public Guid? CustomerAddressId { get; init; }

    public PaymentMethod PaymentMethod { get; init; }

    public decimal Subtotal { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal MinimumOrderValue { get; init; }

    public decimal? FreeShippingThreshold { get; init; }

    public bool FreeShippingApplied { get; init; }

    public decimal Total { get; init; }

    public bool StoreIsOpen { get; init; }
}
