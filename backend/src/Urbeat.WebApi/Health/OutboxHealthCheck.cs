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
    private readonly OutboxHealthCountsCache _countsCache;
    private readonly TimeProvider _timeProvider;
    private readonly bool _runsLocalWorker;

    public OutboxHealthCheck(
        IServiceScopeFactory scopeFactory,
        IOutboxWorkerLiveness liveness,
        IOptions<OutboxOptions> options,
        HangfireRuntime hangfireRuntime,
        OutboxHealthCountsCache countsCache,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _liveness = liveness;
        _options = options.Value;
        _hangfireRuntime = hangfireRuntime;
        _countsCache = countsCache;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // In the split deployment the outbox runs in the worker processes. The in-memory liveness
        // marker of the web process is then meaningless and must not mark the API unhealthy.
        _runsLocalWorker = _options.WorkerEnabled && _hangfireRuntime.RunsOutbox;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Database counters are coalesced and reused for a short window (see
        // OutboxHealthCountsCache). The heartbeat below is always read live from memory, so worker
        // staleness is never hidden by the cache.
        var counts = await _countsCache
            .GetAsync(LoadCountsAsync, cancellationToken)
            .ConfigureAwait(false);

        var messagePending = counts.MessagePending;
        var messageProcessing = counts.MessageProcessing;
        var messageProcessed = counts.MessageProcessed;
        var messageFailed = counts.MessageFailed;

        var deliveryPending = counts.DeliveryPending;
        var deliveryProcessing = counts.DeliveryProcessing;
        var deliveryProcessed = counts.DeliveryProcessed;
        var deliveryFailed = counts.DeliveryFailed;

        var businessPending = counts.BusinessPending;
        var businessFailed = counts.BusinessFailed;
        var communicationsPending = counts.CommunicationsPending;
        var communicationsFailed = counts.CommunicationsFailed;

        var now = _timeProvider.GetUtcNow();
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

    private async Task<OutboxHealthCounts> LoadCountsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Two grouped queries replace the previous twelve independent COUNTs. The status index on
        // OutboxMessages and the (channel, status) index on OutboxHandlerDeliveries keep each
        // aggregate bounded to a single scan, and OutboxHealthCountsCache prevents a burst of
        // probes from repeating them.
        var messageCounts = await dbContext.OutboxMessages
            .AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deliveryCounts = await dbContext.OutboxHandlerDeliveries
            .AsNoTracking()
            .GroupBy(x => new { x.Channel, x.Status })
            .Select(group => new { group.Key.Channel, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var messageByStatus = messageCounts.ToDictionary(x => x.Status, x => x.Count);
        var deliveryByChannelStatus = deliveryCounts.ToDictionary(
            x => (x.Channel, x.Status),
            x => x.Count);

        int MessageCount(OutboxMessageStatus status)
            => messageByStatus.TryGetValue(status, out var count) ? count : 0;

        int DeliveryCount(OutboxHandlerDeliveryStatus status)
            => deliveryCounts.Where(x => x.Status == status).Sum(x => x.Count);

        int ChannelCount(OutboxChannel channel, OutboxHandlerDeliveryStatus status)
            => deliveryByChannelStatus.TryGetValue((channel, status), out var count) ? count : 0;

        // Legacy message projection. Kept for compatibility with existing dashboards; deliveries
        // below are the authoritative processing unit.
        return new OutboxHealthCounts(
            MessagePending: MessageCount(OutboxMessageStatus.Pending),
            MessageProcessing: MessageCount(OutboxMessageStatus.Processing),
            MessageProcessed: MessageCount(OutboxMessageStatus.Processed),
            MessageFailed: MessageCount(OutboxMessageStatus.Failed),
            DeliveryPending: DeliveryCount(OutboxHandlerDeliveryStatus.Pending),
            DeliveryProcessing: DeliveryCount(OutboxHandlerDeliveryStatus.Processing),
            DeliveryProcessed: DeliveryCount(OutboxHandlerDeliveryStatus.Processed),
            DeliveryFailed: DeliveryCount(OutboxHandlerDeliveryStatus.Failed),
            BusinessPending: ChannelCount(OutboxChannel.Business, OutboxHandlerDeliveryStatus.Pending)
                + ChannelCount(OutboxChannel.Business, OutboxHandlerDeliveryStatus.Processing),
            BusinessFailed: ChannelCount(OutboxChannel.Business, OutboxHandlerDeliveryStatus.Failed),
            CommunicationsPending: ChannelCount(OutboxChannel.Communications, OutboxHandlerDeliveryStatus.Pending)
                + ChannelCount(OutboxChannel.Communications, OutboxHandlerDeliveryStatus.Processing),
            CommunicationsFailed: ChannelCount(OutboxChannel.Communications, OutboxHandlerDeliveryStatus.Failed));
    }
}
