namespace Urbeat.Domain.Entities;

/// <summary>
/// Current delivery driver assignment for an order. At most one row may exist per order
/// (enforced by a unique index), which makes the assignment authoritative and prevents two
/// drivers from owning the same delivery. History is preserved in the audit log.
/// </summary>
public sealed class OrderDeliveryAssignment : BaseEntity
{
    public Guid OrderId { get; set; }

    public Guid StoreId { get; set; }

    public Guid DeliveryDriverId { get; set; }

    public DeliveryDriver? DeliveryDriver { get; set; }

    public Guid AssignedByUserId { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    public DateTime? DeliveryStartedAtUtc { get; set; }

    public int PinAttemptCount { get; set; }

    public DateTime? LastPinAttemptAtUtc { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Every PIN attempt and every delivery start updates it,
    /// so concurrent requests cannot overwrite each other's attempt counter or confirm the
    /// same delivery twice. It is not a security secret and carries no business meaning.
    /// </summary>
    public int ConcurrencyVersion { get; set; }

    public void AdvanceConcurrency()
    {
        ConcurrencyVersion += 1;
    }
}
