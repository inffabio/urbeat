using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class OrderDetailsResponseDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public Guid CustomerUserId { get; init; }

    public string? CustomerName { get; init; }

    public string? CustomerPhoneNumber { get; init; }

    public Guid StoreId { get; init; }

    public FulfillmentType FulfillmentType { get; init; }

    public OrderStatus Status { get; init; }

    public PaymentMethod PaymentMethod { get; init; }

    public decimal Subtotal { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal Total { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public string? AddressCep { get; init; }

    public string? AddressStreet { get; init; }

    public string? AddressNumber { get; init; }

    public string? AddressNeighborhood { get; init; }

    public string? AddressCity { get; init; }

    public string? AddressState { get; init; }

    public string? AddressComplement { get; init; }

    public string? AddressReference { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyCollection<OrderItemResponseDto> Items { get; init; } = [];

    public IReadOnlyCollection<OrderStatusHistoryResponseDto> History { get; init; } = [];
}
