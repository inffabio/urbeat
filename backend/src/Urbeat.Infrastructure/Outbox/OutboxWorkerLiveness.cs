using Urbeat.Application.Interfaces;

namespace Urbeat.Infrastructure.Outbox;

/// <summary>
/// Default <see cref="IOutboxWorkerLiveness"/> implementation. The heartbeat is stored as a raw
/// UTC tick count updated with <see cref="Interlocked"/> so concurrent health-check reads never
/// observe a torn value.
/// </summary>
public sealed class OutboxWorkerLiveness : IOutboxWorkerLiveness
{
    private long _lastHeartbeatUtcTicks;

    public DateTimeOffset? LastHeartbeatUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastHeartbeatUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void RecordHeartbeat(DateTimeOffset at)
    {
        Interlocked.Exchange(ref _lastHeartbeatUtcTicks, at.UtcTicks);
    }
}
