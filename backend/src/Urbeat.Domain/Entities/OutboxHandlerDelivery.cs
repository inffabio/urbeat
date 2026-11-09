namespace Urbeat.Domain.Entities;

/// <summary>
/// Durable unit of work for a single <c>IOutboxEventHandler</c> of an outbox message.
///
/// Each outbox message fans out into one delivery per matching handler. Deliveries are claimed and
/// retried independently per <see cref="Channel"/>, so the business and communications workers never
/// race for the same row and a permanent failure in one handler does not block its siblings.
///
/// This is intentionally different from <see cref="OutboxDelivery"/>, which records a completed
/// external side effect (e-mail/SMS) for idempotency purposes.
/// </summary>
public sealed class OutboxHandlerDelivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OutboxMessageId { get; set; }

    /// <summary>Stable handler identity, e.g. <c>business.notification</c>.</summary>
    public string HandlerKey { get; set; } = string.Empty;

    public OutboxChannel Channel { get; set; }

    /// <summary>
    /// Denormalized copy of <see cref="OutboxMessage.AggregateId"/>. It lets the claim query enforce
    /// per-aggregate ordering without joining the message table on every poll.
    /// </summary>
    public Guid? AggregateId { get; set; }

    /// <summary>
    /// Denormalized copy of <see cref="OutboxMessage.Sequence"/>. <c>0</c> means the event is not
    /// ordered and may be claimed immediately.
    /// </summary>
    public long Sequence { get; set; }

    public OutboxHandlerDeliveryStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTime AvailableAtUtc { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
