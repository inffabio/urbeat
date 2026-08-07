namespace Urbeat.Infrastructure.Services.Payments;

public sealed class MercadoPagoCheckoutCreateRequest
{
    public required string ExternalReference { get; init; }

    public required string PayerEmail { get; init; }

    public required IReadOnlyCollection<MercadoPagoCheckoutItem> Items { get; init; }
}

public sealed class MercadoPagoCheckoutItem
{
    public required string Title { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }
}
