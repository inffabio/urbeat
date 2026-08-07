namespace Urbeat.Infrastructure.Services.Payments;

public sealed class MercadoPagoCheckoutCreateResponse
{
    public required string TransactionId { get; init; }

    public required string CheckoutUrl { get; init; }

    public required string RawPayload { get; init; }
}
