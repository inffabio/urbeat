using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class CheckoutRequestDto
{
    public Guid StoreId { get; init; }

    public FulfillmentType FulfillmentType { get; init; }

    public Guid? CustomerAddressId { get; init; }

    public PaymentMethod? PaymentMethod { get; init; }

    /// <summary>
    /// Authoritative total shown by the preview the customer accepted. On confirmation, a different
    /// server total returns 409 and no order is persisted. Null is the explicit compatibility path
    /// for older clients that predate this field; the server still recomputes and persists its own
    /// authoritative total. New clients always send it.
    /// </summary>
    public decimal? ExpectedTotal { get; init; }

    /// <summary>
    /// Cash change choice. Required when <see cref="PaymentMethod"/> is cash and must be null for
    /// every other method. True means no change is needed; false means
    /// <see cref="CashChangeForAmount"/> was informed.
    /// </summary>
    public bool? CashWithoutChange { get; init; }

    /// <summary>
    /// Amount the customer wants change for. Required when <see cref="CashWithoutChange"/> is false
    /// and validated against the authoritative total by the server.
    /// </summary>
    public decimal? CashChangeForAmount { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyCollection<CheckoutItemRequestDto> Items { get; init; } = [];
}
