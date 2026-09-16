namespace Urbeat.Infrastructure.Services.Payments;

public sealed class MercadoPagoPaymentDetails
{
    public required string TransactionId { get; init; }

    public required string Status { get; init; }

    /// <summary>
    /// Authoritative amount reported by the gateway for this transaction. A real gateway must report
    /// it; when it is null the webhook refuses to mark the payment as paid. Only the explicitly
    /// simulated fake/mock gateway (see <see cref="IsSimulated"/>) may omit it.
    /// </summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// ISO currency reported by the gateway (for example <c>BRL</c>). Null when unknown.
    /// </summary>
    public string? CurrencyId { get; init; }

    /// <summary>
    /// True only for the local fake/mock gateway path, which does not report an amount or currency.
    /// This flag is set in code by the adapter and is never derived from the external payload, so a
    /// real gateway response can never opt out of the amount/currency validation.
    /// </summary>
    public bool IsSimulated { get; init; }

    public string RawPayload { get; init; } = string.Empty;
}
