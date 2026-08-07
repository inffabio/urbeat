using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class CheckoutConfirmResponseDto
{
    public Guid OrderId { get; init; }

    public string Code { get; init; } = string.Empty;

    public FulfillmentType FulfillmentType { get; init; }

    public OrderStatus Status { get; init; }

    public decimal Subtotal { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal Total { get; init; }
}
