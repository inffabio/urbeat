using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.WebApi.Operations;

/// <summary>
/// Read-only projection of the transactional outbox for the administrative operations panel.
/// It performs exactly two aggregate queries (handler deliveries and messages), never selects
/// payloads, recipients, errors or aggregate identifiers, and derives backlog ages from UTC
/// timestamps only.
/// </summary>
public sealed class OutboxOperationsSnapshotReader
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public OutboxOperationsSnapshotReader(ApplicationDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<OutboxOperationsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var deliveryGroups = await _dbContext.OutboxHandlerDeliveries
            .AsNoTracking()
            .GroupBy(x => new { x.Channel, x.Status })
            .Select(group => new
            {
                group.Key.Channel,
                group.Key.Status,
                Count = group.Count(),
                // Age means time since the row became processable, not time since it was created.
                OldestAtUtc = group.Min(x => x.AvailableAtUtc)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var messageGroups = await _dbContext.OutboxMessages
            .AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count(),
                // AvailableAtUtc is the start of the actionable backlog window.
                OldestAtUtc = group.Min(x => x.AvailableAtUtc)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deliveryRows = deliveryGroups
            .Select(row => new DeliveryGroupRow(row.Channel, row.Status, row.Count, ToUtc(row.OldestAtUtc)))
            .ToList();

        var messageRows = messageGroups
            .Select(row => new MessageGroupRow(row.Status, row.Count, ToUtc(row.OldestAtUtc)))
            .ToList();

        var now = _timeProvider.GetUtcNow();
        var business = BuildChannel(deliveryRows, OutboxChannel.Business, now);
        var communications = BuildChannel(deliveryRows, OutboxChannel.Communications, now);
        var messages = BuildMessages(messageRows, now);

        var oldestBacklog = Oldest(
            business.OldestBacklogAtUtc,
            communications.OldestBacklogAtUtc);

        return new OutboxOperationsSnapshot(
            Totals: new OutboxTotals(
                Pending: business.Pending + communications.Pending,
                Processing: business.Processing + communications.Processing,
                Processed: business.Processed + communications.Processed,
                Failed: business.Failed + communications.Failed,
                OldestBacklogAtUtc: oldestBacklog,
                OldestBacklogAgeSeconds: AgeSeconds(now, oldestBacklog)),
            Business: business,
            Communications: communications,
            Messages: messages);
    }

    private static OutboxChannelOperations BuildChannel(
        IReadOnlyCollection<DeliveryGroupRow> rows,
        OutboxChannel channel,
        DateTimeOffset now)
    {
        var channelRows = rows.Where(row => row.Channel == channel).ToList();

        int Count(OutboxHandlerDeliveryStatus status) =>
            channelRows.Where(row => row.Status == status).Sum(row => row.Count);

        var oldest = Oldest(
            channelRows
                .Where(row => OutboxBacklogPolicy.IsBacklog(row.Status))
                .Select(row => row.OldestAtUtc));

        return new OutboxChannelOperations(
            Channel: channel.ToString(),
            Pending: Count(OutboxHandlerDeliveryStatus.Pending),
            Processing: Count(OutboxHandlerDeliveryStatus.Processing),
            Processed: Count(OutboxHandlerDeliveryStatus.Processed),
            Failed: Count(OutboxHandlerDeliveryStatus.Failed),
            OldestBacklogAtUtc: oldest,
            OldestBacklogAgeSeconds: AgeSeconds(now, oldest));
    }

    private static OutboxMessageTotals BuildMessages(
        IReadOnlyCollection<MessageGroupRow> rows,
        DateTimeOffset now)
    {
        int Count(OutboxMessageStatus status) =>
            rows.Where(row => row.Status == status).Sum(row => row.Count);

        var oldest = Oldest(
            rows
                .Where(row => OutboxBacklogPolicy.IsBacklog(row.Status))
                .Select(row => row.OldestAtUtc));

        return new OutboxMessageTotals(
            Pending: Count(OutboxMessageStatus.Pending),
            Processing: Count(OutboxMessageStatus.Processing),
            Processed: Count(OutboxMessageStatus.Processed),
            Failed: Count(OutboxMessageStatus.Failed),
            OldestBacklogAtUtc: oldest,
            OldestBacklogAgeSeconds: AgeSeconds(now, oldest));
    }

    private static DateTime? Oldest(params DateTime?[] values) =>
        Oldest(values.AsEnumerable());

    private static DateTime? Oldest(IEnumerable<DateTime?> values)
    {
        DateTime? oldest = null;
        foreach (var value in values)
        {
            if (value is null) continue;
            if (oldest is null || value < oldest) oldest = value;
        }

        return ToUtc(oldest);
    }

    private static long? AgeSeconds(DateTimeOffset now, DateTime? utc)
    {
        if (utc is null) return null;
        var age = now - new DateTimeOffset(utc.Value, TimeSpan.Zero);
        return age <= TimeSpan.Zero ? 0 : (long)Math.Floor(age.TotalSeconds);
    }

    private static DateTime? ToUtc(DateTime? value) =>
        value is null
            ? null
            : value.Value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private sealed record DeliveryGroupRow(
        OutboxChannel Channel,
        OutboxHandlerDeliveryStatus Status,
        int Count,
        DateTime? OldestAtUtc);

    private sealed record MessageGroupRow(
        OutboxMessageStatus Status,
        int Count,
        DateTime? OldestAtUtc);
}

public sealed record OutboxOperationsSnapshot(
    OutboxTotals Totals,
    OutboxChannelOperations Business,
    OutboxChannelOperations Communications,
    OutboxMessageTotals Messages);
