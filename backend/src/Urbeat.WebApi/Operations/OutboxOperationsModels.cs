namespace Urbeat.WebApi.Operations;

/// <summary>
/// Canonical status values returned by the administrative operations endpoint. The API never
/// returns exception messages, payloads or connection data; consumers map these values to labels.
/// </summary>
public static class OperationsStatus
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string Unhealthy = "unhealthy";
    public const string Unknown = "unknown";
    public const string NotConfigured = "not_configured";
}

public sealed record OutboxOperationsResponse(
    DateTime GeneratedAtUtc,
    string Status,
    int BacklogThreshold,
    OutboxTotals Totals,
    OutboxChannelOperations Business,
    OutboxChannelOperations Communications,
    OutboxMessageTotals Messages,
    OutboxWorkerMonitoring Workers,
    IReadOnlyList<OperationsDependencyStatus> Dependencies);

public sealed record OutboxTotals(
    int Pending,
    int Processing,
    int Processed,
    int Failed,
    DateTime? OldestBacklogAtUtc,
    long? OldestBacklogAgeSeconds);

public sealed record OutboxChannelOperations(
    string Channel,
    int Pending,
    int Processing,
    int Processed,
    int Failed,
    DateTime? OldestBacklogAtUtc,
    long? OldestBacklogAgeSeconds);

public sealed record OutboxMessageTotals(
    int Pending,
    int Processing,
    int Processed,
    int Failed,
    DateTime? OldestBacklogAtUtc,
    long? OldestBacklogAgeSeconds);

public sealed record OutboxWorkerMonitoring(
    bool Available,
    string Status,
    string? Message,
    IReadOnlyList<OutboxWorkerStatus> Workers);

public sealed record OutboxWorkerStatus(
    string Role,
    IReadOnlyList<string> Queues,
    DateTime? LastHeartbeatUtc,
    long? HeartbeatAgeSeconds,
    string Status,
    string Source);

public sealed record OperationsDependencyStatus(
    string Name,
    string Status,
    long? DurationMs,
    string? Message);
