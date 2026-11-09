using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Hangfire;
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
/// heartbeated) is reported unhealthy regardless of the backlog. Database reachability, message and
/// handler-delivery counts (never payloads), per-channel backlog/failures, and the backlog threshold
/// are secondary signals.
/// </summary>
public sealed class OutboxHealthCheck : IHealthCheck
{
    public const string Name = "outbox";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxWorkerLiveness _liveness;
    private readonly OutboxOptions _options;
    private readonly HangfireRuntime _hangfireRuntime;
    private readonly bool _runsLocalWorker;

    public OutboxHealthCheck(
        IServiceScopeFactory scopeFactory,
        IOutboxWorkerLiveness liveness,
        IOptions<OutboxOptions> options,
        HangfireRuntime hangfireRuntime)
    {
        _scopeFactory = scopeFactory;
        _liveness = liveness;
        _options = options.Value;
        _hangfireRuntime = hangfireRuntime;

        // In the split deployment the outbox runs in the worker processes. The in-memory liveness
        // marker of the web process is then meaningless and must not mark the API unhealthy.
        _runsLocalWorker = _options.WorkerEnabled && _hangfireRuntime.RunsOutbox;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Legacy message projection. Kept for compatibility with existing dashboards; deliveries
        // below are the authoritative processing unit.
        var messagePending = await dbContext.OutboxMessages
            .CountAsync(x => x.Status == OutboxMessageStatus.Pending, cancellationToken);
        var messageProcessing = await dbContext.OutboxMessages
            .CountAsync(x => x.Status == OutboxMessageStatus.Processing, cancellationToken);
        var messageProcessed = await dbContext.OutboxMessages
            .CountAsync(x => x.Status == OutboxMessageStatus.Processed, cancellationToken);
        var messageFailed = await dbContext.OutboxMessages
            .CountAsync(x => x.Status == OutboxMessageStatus.Failed, cancellationToken);

        var deliveryPending = await dbContext.OutboxHandlerDeliveries
            .CountAsync(x => x.Status == OutboxHandlerDeliveryStatus.Pending, cancellationToken);
        var deliveryProcessing = await dbContext.OutboxHandlerDeliveries
            .CountAsync(x => x.Status == OutboxHandlerDeliveryStatus.Processing, cancellationToken);
        var deliveryProcessed = await dbContext.OutboxHandlerDeliveries
            .CountAsync(x => x.Status == OutboxHandlerDeliveryStatus.Processed, cancellationToken);
        var deliveryFailed = await dbContext.OutboxHandlerDeliveries
            .CountAsync(x => x.Status == OutboxHandlerDeliveryStatus.Failed, cancellationToken);

        var businessPending = await dbContext.OutboxHandlerDeliveries
            .CountAsync(x => x.Channel == OutboxChannel.Business
                && (x.Status == OutboxHandlerDeliveryStatus.Pending
                    || x.Status == OutboxHandlerDeliveryStatus.Processing), cancellationToken);
        var businessFailed = await dbContext.OutboxHandlerDeliveries
            .CountAsync(x => x.Channel == OutboxChannel.Business
                && x.Status == OutboxHandlerDeliveryStatus.Failed, cancellationToken);
        var communicationsPending = await dbContext.OutboxHandlerDeliveries
            .CountAsync(x => x.Channel == OutboxChannel.Communications
                && (x.Status == OutboxHandlerDeliveryStatus.Pending
                    || x.Status == OutboxHandlerDeliveryStatus.Processing), cancellationToken);
        var communicationsFailed = await dbContext.OutboxHandlerDeliveries
            .CountAsync(x => x.Channel == OutboxChannel.Communications
                && x.Status == OutboxHandlerDeliveryStatus.Failed, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var lastHeartbeat = _liveness.LastHeartbeatUtc;
        var totalMessageBacklog = messagePending + messageProcessing;

        var data = new Dictionary<string, object>
        {
            // Legacy message-level keys (kept for existing consumers).
            ["pending"] = totalMessageBacklog,
            ["failed"] = messageFailed,
            ["processing"] = messageProcessing,
            ["processed"] = messageProcessed,

            // Authoritative handler-delivery counts.
            ["deliveriesPending"] = deliveryPending,
            ["deliveriesProcessing"] = deliveryProcessing,
            ["deliveriesProcessed"] = deliveryProcessed,
            ["deliveriesFailed"] = deliveryFailed,
            ["businessPending"] = businessPending,
            ["businessFailed"] = businessFailed,
            ["communicationsPending"] = communicationsPending,
            ["communicationsFailed"] = communicationsFailed,

            ["workerEnabled"] = _options.WorkerEnabled,
            ["workerRunsLocally"] = _runsLocalWorker,
            ["hangfireRole"] = _hangfireRuntime.Role.ToString(),
            ["outboxChannels"] = string.Join(",", _hangfireRuntime.OutboxChannels),
            ["lastHeartbeatUtc"] = _runsLocalWorker
                ? lastHeartbeat?.ToString("o") ?? "never"
                : "not-applicable"
        };

        if (_runsLocalWorker)
        {
            if (lastHeartbeat is null || now - lastHeartbeat.Value > _options.WorkerStaleAfter)
            {
                return HealthCheckResult.Unhealthy(
                    "Outbox worker has no recent heartbeat; it may be stalled.",
                    data: data);
            }
        }

        if (deliveryFailed > 0 || messageFailed > 0)
        {
            return HealthCheckResult.Degraded(
                $"Outbox has {deliveryFailed} failed delivery(ies) and {messageFailed} failed message(s) awaiting review.",
                data: data);
        }

        if (deliveryPending > _options.BacklogThreshold || totalMessageBacklog > _options.BacklogThreshold)
        {
            return HealthCheckResult.Degraded(
                $"Outbox backlog of {deliveryPending} pending delivery(ies) / {totalMessageBacklog} message(s) exceeds threshold {_options.BacklogThreshold}.",
                data: data);
        }

        return HealthCheckResult.Healthy("Outbox worker healthy.", data);
    }
}
