namespace Urbeat.Domain.Entities;

/// <summary>
/// Records a completed external delivery for an outbox message. The unique
/// <see cref="DeliveryKey"/> is a best-effort idempotency boundary: once a handler has persisted a
/// delivery for a key, a redelivery of the same event is normally skipped instead of repeating the
/// side effect (e-mail, SMS, …). This is not exactly-once — providers without server-side
/// idempotency can still duplicate a send if a crash occurs between the send and this record.
/// <see cref="ProviderMessageId"/> stores the provider/message identifier for diagnostics when the
/// adapter returns one.
/// </summary>
public sealed class OutboxDelivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OutboxMessageId { get; set; }

    public string DeliveryKey { get; set; } = string.Empty;

    public string HandlerType { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public string? ProviderMessageId { get; set; }

    public DateTime DeliveredAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
