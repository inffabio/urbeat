namespace Urbeat.WebApi.Health;

/// <summary>
/// Immutable snapshot of the outbox counters used by <see cref="OutboxHealthCheck"/>. The snapshot
/// is cached for a few seconds so a burst of health probes performs at most one pair of aggregate
/// queries instead of two queries per probe.
/// </summary>
public sealed record OutboxHealthCounts(
    int MessagePending,
    int MessageProcessing,
    int MessageProcessed,
    int MessageFailed,
    int DeliveryPending,
    int DeliveryProcessing,
    int DeliveryProcessed,
    int DeliveryFailed,
    int BusinessPending,
    int BusinessFailed,
    int CommunicationsPending,
    int CommunicationsFailed);
