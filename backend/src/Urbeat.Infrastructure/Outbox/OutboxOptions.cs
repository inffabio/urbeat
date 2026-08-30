namespace Urbeat.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool WorkerEnabled { get; set; } = true;

    public int BatchSize { get; set; } = 20;

    public int MaxAttempts { get; set; } = 5;

    public int BacklogThreshold { get; set; } = 1000;

    public TimeSpan LockDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum allowed gap between the outbox worker's heartbeats before the worker is considered
    /// stale. Used by <c>OutboxHealthCheck</c> to report real liveness rather than only backlog.
    /// </summary>
    public TimeSpan WorkerStaleAfter { get; set; } = TimeSpan.FromMinutes(2);
}
