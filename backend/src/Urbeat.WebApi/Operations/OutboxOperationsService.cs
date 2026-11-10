using Urbeat.Infrastructure.Outbox;
using Urbeat.WebApi.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Urbeat.WebApi.Operations;

/// <summary>
/// Builds the sanitized administrative operations projection. Counts and backlog ages come from
/// <see cref="OutboxOperationsSnapshotReader"/>; infrastructure status comes from the health checks
/// tagged <c>operations</c>; worker liveness comes from <see cref="OutboxWorkerProbe"/>.
///
/// No exception message, payload, recipient, SQL text, connection string or server name is copied
/// into the response. Rate metrics are intentionally absent: the current model has no reliable
/// rolling window and the panel must not invent one.
/// </summary>
public sealed class OutboxOperationsService
{
    private const string OperationsTag = "operations";

    private readonly OutboxOperationsSnapshotCache _snapshotCache;
    private readonly OutboxWorkerProbe _workerProbe;
    private readonly HealthCheckService _healthChecks;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxOperationsService> _logger;

    public OutboxOperationsService(
        OutboxOperationsSnapshotCache snapshotCache,
        OutboxWorkerProbe workerProbe,
        HealthCheckService healthChecks,
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        ILogger<OutboxOperationsService> logger)
    {
        _snapshotCache = snapshotCache;
        _workerProbe = workerProbe;
        _healthChecks = healthChecks;
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger;
    }

    public async Task<OutboxOperationsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _snapshotCache.GetAsync(cancellationToken).ConfigureAwait(false);
        // Hangfire's monitoring API is synchronous. It is intentionally called directly rather
        // than wrapped in Task.Run; the short projection cache prevents polling bursts.
        var workers = _workerProbe.Read();
        var dependencies = await ReadDependenciesAsync(cancellationToken).ConfigureAwait(false);
        var outboxStatus = EvaluateOutboxStatus(snapshot);
        var status = AggregateStatus(outboxStatus, workers, dependencies);
        var now = _timeProvider.GetUtcNow();

        return new OutboxOperationsResponse(
            GeneratedAtUtc: now.UtcDateTime,
            Status: status,
            BacklogThreshold: _options.BacklogThreshold,
            Totals: snapshot.Totals,
            Business: snapshot.Business,
            Communications: snapshot.Communications,
            Messages: snapshot.Messages,
            Workers: workers,
            Dependencies: dependencies);
    }

    private async Task<IReadOnlyList<OperationsDependencyStatus>> ReadDependenciesAsync(
        CancellationToken cancellationToken)
    {
        var dependencies = new List<OperationsDependencyStatus>
        {
            // The endpoint is answering, so the API process itself is reachable. This is not a
            // substitute for the readiness probe, which also validates PostgreSQL and the outbox.
            new("api", OperationsStatus.Healthy, DurationMs: null, Message: null)
        };

        try
        {
            var report = await _healthChecks
                .CheckHealthAsync(
                    registration => registration.Tags.Contains(OperationsTag),
                    cancellationToken)
                .ConfigureAwait(false);

            dependencies.Add(ToDependency(name: PostgresHealthCheck.Name, report));
            dependencies.Add(ToDependency(name: RedisHealthCheck.Name, report));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The health service is an observability dependency; a probe failure must not turn the
            // operations panel into a 500. Return unknown entries without exposing the exception.
            _logger.LogWarning(
                "Operations dependency health probe failed ({ErrorType}).",
                ex.GetType().Name);

            dependencies.Add(new OperationsDependencyStatus(
                PostgresHealthCheck.Name,
                OperationsStatus.Unknown,
                DurationMs: null,
                Message: "Não foi possível verificar o PostgreSQL."));
            dependencies.Add(new OperationsDependencyStatus(
                RedisHealthCheck.Name,
                OperationsStatus.Unknown,
                DurationMs: null,
                Message: "Não foi possível verificar o Redis."));
        }

        return dependencies;
    }

    private static OperationsDependencyStatus ToDependency(string name, HealthReport report)
    {
        if (!report.Entries.TryGetValue(name, out var entry))
        {
            return new OperationsDependencyStatus(
                name,
                OperationsStatus.Unknown,
                DurationMs: null,
                Message: "Verificação não executada.");
        }

        return new OperationsDependencyStatus(
            name,
            MapStatus(entry.Status),
            DurationMs: (long)Math.Round(entry.Duration.TotalMilliseconds),
            Message: null);
    }

    private string EvaluateOutboxStatus(OutboxOperationsSnapshot snapshot)
    {
        if (snapshot.Totals.Failed > 0 || snapshot.Messages.Failed > 0)
        {
            return OperationsStatus.Degraded;
        }

        var deliveryBacklog = OutboxBacklogPolicy.Count(snapshot.Totals.Pending, snapshot.Totals.Processing);
        var messageBacklog = OutboxBacklogPolicy.Count(snapshot.Messages.Pending, snapshot.Messages.Processing);

        return deliveryBacklog > _options.BacklogThreshold || messageBacklog > _options.BacklogThreshold
            ? OperationsStatus.Degraded
            : OperationsStatus.Healthy;
    }

    private static string AggregateStatus(
        string outboxStatus,
        OutboxWorkerMonitoring workers,
        IReadOnlyList<OperationsDependencyStatus> dependencies)
    {
        var statuses = dependencies
            .Select(dependency => dependency.Status)
            .Append(outboxStatus)
            .Append(workers.Status)
            .ToList();

        if (statuses.Contains(OperationsStatus.Unhealthy))
        {
            return OperationsStatus.Unhealthy;
        }

        if (statuses.Any(status =>
                status is OperationsStatus.Degraded or OperationsStatus.Unknown or OperationsStatus.NotConfigured))
        {
            return OperationsStatus.Degraded;
        }

        return OperationsStatus.Healthy;
    }

    private static string MapStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => OperationsStatus.Healthy,
        HealthStatus.Degraded => OperationsStatus.Degraded,
        _ => OperationsStatus.Unhealthy
    };
}
