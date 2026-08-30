namespace Urbeat.Domain.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Type { get; set; } = string.Empty;

    public Guid? AggregateId { get; set; }

    public string? AggregateType { get; set; }

    /// <summary>
    /// Monotonic per-aggregate ordering value. <c>0</c> means "no ordering constraint" and is used
    /// by events that do not participate in per-order sequencing (e.g. outbound message requests).
    /// Order events use an increasing value (1 for creation, then 2, 3, …) so the dispatcher never
    /// claims a later event while an earlier one is still pending or processing.
    /// </summary>
    public long Sequence { get; set; }

    public string Payload { get; set; } = "{}";

    public DateTime OccurredAtUtc { get; set; }

    public DateTime AvailableAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    public string? LastError { get; set; }

    public OutboxMessageStatus Status { get; set; }
}
