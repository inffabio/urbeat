namespace Urbeat.Domain.Entities;

public sealed class Payment : BaseEntity
{
    public Guid OrderId { get; set; }

    public PaymentGateway Gateway { get; set; }

    public string GatewayTransactionId { get; set; } = string.Empty;

    public string? GatewayCheckoutUrl { get; set; }

    public string? ExternalReference { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }

    public PaymentStatus Status { get; set; }

    public string? RawPayload { get; set; }
}
