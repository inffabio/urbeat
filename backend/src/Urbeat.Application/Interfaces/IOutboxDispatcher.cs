using Urbeat.Domain.Entities;

namespace Urbeat.Application.Interfaces;

public interface IOutboxDispatcher
{
    /// <summary>
    /// Claims and processes one bounded batch of deliveries for <paramref name="channel"/>. A host
    /// must only pass the channel(s) it owns so the business and communications workers never
    /// process each other's deliveries.
    /// </summary>
    Task<int> DispatchBatchAsync(OutboxChannel channel, CancellationToken cancellationToken = default);
}
