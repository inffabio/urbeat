namespace Urbeat.Application.Interfaces;

public interface IOutboxWriter
{
    Task EnqueueAsync<T>(
        string type,
        Guid aggregateId,
        T payload,
        DateTime occurredAtUtc,
        long sequence = 0,
        string? aggregateType = null,
        CancellationToken cancellationToken = default);
}
