using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class OrderSummaryResponseDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public Guid StoreId { get; init; }

    public string? CustomerName { get; init; }

    public string? CustomerPhoneNumber { get; init; }

    public FulfillmentType? FulfillmentType { get; init; }

    public PaymentMethod? PaymentMethod { get; init; }

    public string? AddressSummary { get; init; }

    public string? ItemsSummary { get; init; }

    public OrderStatus Status { get; init; }

    public decimal Subtotal { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal Total { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? DeliveryConfirmedAtUtc { get; init; }

    public DateTime? SellerCompletedAtUtc { get; init; }

    public Guid? DeliveryDriverId { get; init; }

    public string? DeliveryDriverName { get; init; }

    public DateTime? DeliveryStartedAtUtc { get; init; }

    public DateTime? DeliveryPinConfirmedAtUtc { get; init; }

    public Guid? DeliveryRouteId { get; init; }

    public DeliveryRouteStatus? DeliveryRouteStatus { get; init; }
}
