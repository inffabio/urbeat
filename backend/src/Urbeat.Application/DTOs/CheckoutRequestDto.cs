using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class CheckoutRequestDto
{
    public Guid StoreId { get; init; }

    public FulfillmentType FulfillmentType { get; init; }

    public Guid? CustomerAddressId { get; init; }

    public PaymentMethod? PaymentMethod { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyCollection<CheckoutItemRequestDto> Items { get; init; } = [];
}
