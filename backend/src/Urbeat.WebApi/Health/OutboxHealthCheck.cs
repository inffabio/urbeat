using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Urbeat.WebApi.Health;

/// <summary>
/// Reports outbox health. When the worker is enabled, liveness is the primary signal: a worker whose
/// most recent heartbeat is older than <c>OutboxOptions.WorkerStaleAfter</c> (or that has never
/// heartbeated) is reported unhealthy regardless of the backlog. Database reachability, pending and
/// failed message counts (never payloads), and the backlog threshold are secondary signals.
/// </summary>
public sealed class OutboxHealthCheck : IHealthCheck
{
    public const string Name = "outbox";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxWorkerLiveness _liveness;
    private readonly OutboxOptions _options;

    public OutboxHealthCheck(
        IServiceScopeFactory scopeFactory,
        IOutboxWorkerLiveness liveness,
        IOptions<OutboxOptions> options)
    {
        _scopeFactory = scopeFactory;
        _liveness = liveness;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = await dbContext.OutboxMessages
            .CountAsync(x => x.Status == OutboxMessageStatus.Pending || x.Status == OutboxMessageStatus.Processing, cancellationToken);

        var failed = await dbContext.OutboxMessages
            .CountAsync(x => x.Status == OutboxMessageStatus.Failed, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var lastHeartbeat = _liveness.LastHeartbeatUtc;

        var data = new Dictionary<string, object>
        {
            ["pending"] = pending,
            ["failed"] = failed,
            ["workerEnabled"] = _options.WorkerEnabled,
            ["lastHeartbeatUtc"] = lastHeartbeat?.ToString("o") ?? "never"
        };

        if (_options.WorkerEnabled)
        {
            if (lastHeartbeat is null || now - lastHeartbeat.Value > _options.WorkerStaleAfter)
            {
                return HealthCheckResult.Unhealthy(
                    "Outbox worker has no recent heartbeat; it may be stalled.",
                    data: data);
            }
        }

        if (failed > 0)
        {
            return HealthCheckResult.Degraded($"Outbox has {failed} failed message(s) awaiting review.", data: data);
        }

        if (pending > _options.BacklogThreshold)
        {
            return HealthCheckResult.Degraded($"Outbox backlog of {pending} message(s) exceeds threshold {_options.BacklogThreshold}.", data: data);
        }

        return HealthCheckResult.Healthy("Outbox worker healthy.", data);
    }
}
