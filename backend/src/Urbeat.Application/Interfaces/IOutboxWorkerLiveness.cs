namespace Urbeat.Application.Interfaces;

/// <summary>
/// Thread-safe heartbeat the outbox worker publishes so the health check can distinguish a live,
/// idle worker from one that is stalled or disabled. A worker records a heartbeat on every poll
/// iteration; the health check compares the gap against <c>OutboxOptions.WorkerStaleAfter</c>.
/// </summary>
public interface IOutboxWorkerLiveness
{
    /// <summary>UTC timestamp of the worker's most recent heartbeat, or <c>null</c> if it has never run.</summary>
    DateTimeOffset? LastHeartbeatUtc { get; }

    void RecordHeartbeat(DateTimeOffset at);
}
