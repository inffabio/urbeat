namespace Urbeat.Application.Interfaces;

/// <summary>
/// Best-effort deduplication for external deliveries performed by outbox handlers. A handler derives
/// a deterministic <see cref="DeliveryKey"/> for a side effect and records it only after a successful
/// send. The unique index on the key suppresses most redeliveries, but this is <b>not</b> an
/// exactly-once guarantee: providers without server-side idempotency (e.g. SMTP) can still duplicate
/// a send when a crash occurs between the send and the record. Handlers must therefore be written to
/// tolerate at-least-once delivery, and SignalR handlers do not use this tracker at all.
/// </summary>
public interface IOutboxDeliveryTracker
{
    Task<bool> IsDeliveredAsync(string deliveryKey, CancellationToken cancellationToken = default);

    Task RecordDeliveredAsync(
        string deliveryKey,
        string handlerType,
        Guid outboxMessageId,
        int attemptCount,
        string? providerMessageId,
        CancellationToken cancellationToken = default);
}
