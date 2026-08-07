namespace Urbeat.Domain.Entities;

public sealed class PaymentWebhookEvent : BaseEntity
{
    public PaymentGateway Gateway { get; set; }

    public string EventKey { get; set; } = string.Empty;

    public string? GatewayTransactionId { get; set; }

    public string Payload { get; set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}
