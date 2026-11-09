namespace Urbeat.Domain.Entities;

/// <summary>
/// A single inbound event extracted from a WhatsApp Cloud API webhook payload. The
/// (<see cref="Provider"/>, <see cref="EventKey"/>) pair is unique and is the idempotency guard:
/// Meta retries the whole envelope, but a message id or a message status must be recorded once.
/// </summary>
public sealed class WhatsAppWebhookEvent : BaseEntity
{
    public const string MetaProvider = "meta";

    public string Provider { get; set; } = MetaProvider;

    public string EventKey { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string? MessageId { get; set; }

    public string? PhoneNumberId { get; set; }

    public string? SenderWaId { get; set; }

    public string? EventTimestamp { get; set; }

    public string Payload { get; set; } = string.Empty;

    public WhatsAppWebhookEventStatus Status { get; set; } = WhatsAppWebhookEventStatus.Received;

    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAtUtc { get; set; }

    public string? ProcessingError { get; set; }
}
