namespace Urbeat.Domain.Entities;

/// <summary>
/// Lifecycle of a single handler delivery. This mirrors <see cref="OutboxMessageStatus"/> but is
/// tracked independently per handler, so one failing handler never rolls back its siblings.
/// </summary>
public enum OutboxHandlerDeliveryStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3
}
