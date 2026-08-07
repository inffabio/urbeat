namespace Urbeat.Infrastructure.Services.Payments;

public sealed class MercadoPagoPaymentDetails
{
    public required string TransactionId { get; init; }

    public required string Status { get; init; }

    public string RawPayload { get; init; } = string.Empty;
}
