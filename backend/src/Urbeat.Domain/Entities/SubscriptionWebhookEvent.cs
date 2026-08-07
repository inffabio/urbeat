namespace Urbeat.Domain.Entities;

public sealed class SubscriptionWebhookEvent : BaseEntity
{
    public string EventKey { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public Guid? SellerUserId { get; set; }

    public string? ExternalReference { get; set; }

    public string Payload { get; set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}