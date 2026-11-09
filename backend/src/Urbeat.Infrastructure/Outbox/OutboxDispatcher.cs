using System.Diagnostics;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Outbox;

/// <summary>
/// Claims and processes outbox handler deliveries for a single <see cref="OutboxChannel"/>.
///
/// Fan-out is lazy and transactional: before claiming, the dispatcher creates one delivery per
/// matching handler for messages that do not have deliveries yet. This backfills messages written
/// before the delivery table existed without touching their payload, status, or ordering. Terminal
/// legacy messages (Processed/Failed) are never fanned out again, so already-delivered events are
/// not replayed.
///
/// Claims use PostgreSQL <c>FOR UPDATE SKIP LOCKED</c> when the provider is relational and an
/// in-memory emulation otherwise. The unique <c>(OutboxMessageId, HandlerKey)</c> index keeps
/// concurrent fan-out idempotent, and the per-aggregate/sequence predicate keeps ordered events of
/// the same channel from overtaking an earlier pending delivery.
/// </summary>
public sealed class OutboxDispatcher : IOutboxDispatcher
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IReadOnlyList<HandlerEntry> _handlers;
    private readonly IReadOnlyDictionary<string, HandlerEntry> _handlersByKey;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        ApplicationDbContext dbContext,
        IEnumerable<IOutboxEventHandler> handlers,
        IOutboxHandlerClassifier classifier,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;

        _handlers = handlers
            .Select(handler => new HandlerEntry(handler, classifier.Classify(handler)))
            .ToList();

        var duplicateKeys = _handlers
            .GroupBy(x => x.Descriptor.HandlerKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate outbox handler keys registered: {string.Join(", ", duplicateKeys)}. " +
                "Each handler must have a unique durable key.");
        }

        _handlersByKey = _handlers.ToDictionary(x => x.Descriptor.HandlerKey, StringComparer.Ordinal);
    }

    public async Task<int> DispatchBatchAsync(
        OutboxChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown outbox channel.");
        }

        var now = DateTime.UtcNow;

        // First touch: create the delivery fan-out for every legacy/new message that still lacks
        // deliveries. This is idempotent and runs before the claim so a message is only processed
        // after all of its handler deliveries exist.
        await BackfillDeliveriesAsync(now, cancellationToken);

        var claimed = await ClaimBatchAsync(channel, now, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        foreach (var delivery in claimed)
        {
            await ProcessDeliveryAsync(delivery, channel, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task<List<OutboxHandlerDelivery>> ClaimBatchAsync(
        OutboxChannel channel,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var lockUntil = now.Add(_options.LockDuration);

        if (_dbContext.Database.IsRelational())
        {
            return await ClaimBatchRelationalAsync(channel, now, lockUntil, cancellationToken);
        }

        return await ClaimBatchInMemoryAsync(channel, now, lockUntil, cancellationToken);
    }

    private async Task<List<OutboxHandlerDelivery>> ClaimBatchInMemoryAsync(
        OutboxChannel channel,
        DateTime now,
        DateTime lockUntil,
        CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.OutboxHandlerDeliveries
            .Where(x => x.Channel == channel
                && ((x.Status == OutboxHandlerDeliveryStatus.Pending && x.AvailableAtUtc <= now)
                    || (x.Status == OutboxHandlerDeliveryStatus.Processing && x.LockedUntilUtc <= now)))
            .OrderBy(x => x.AvailableAtUtc)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new List<OutboxHandlerDelivery>();
        }

        // Per-aggregate ordering within the channel: an ordered (Sequence > 0) candidate is only
        // eligible when no earlier sequence for the same aggregate is still pending/processing.
        var activeSequences = await _dbContext.OutboxHandlerDeliveries
            .Where(x => x.Channel == channel
                && x.Sequence > 0
                && x.AggregateId != null
                && (x.Status == OutboxHandlerDeliveryStatus.Pending
                    || x.Status == OutboxHandlerDeliveryStatus.Processing))
            .Select(x => new { x.AggregateId, x.Sequence })
            .ToListAsync(cancellationToken);

        var lowestActiveByAggregate = activeSequences
            .GroupBy(x => x.AggregateId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Sequence));

        var eligible = candidates
            .Where(x => x.Sequence == 0
                || x.AggregateId is null
                || !lowestActiveByAggregate.TryGetValue(x.AggregateId.Value, out var lowest)
                || lowest >= x.Sequence)
            .Take(_options.BatchSize)
            .ToList();

        foreach (var candidate in eligible)
        {
            candidate.Status = OutboxHandlerDeliveryStatus.Processing;
            candidate.LockedUntilUtc = lockUntil;
        }

        if (eligible.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return eligible;
    }

    private async Task<List<OutboxHandlerDelivery>> ClaimBatchRelationalAsync(
        OutboxChannel channel,
        DateTime now,
        DateTime lockUntil,
        CancellationToken cancellationToken)
    {
        // Fan-out may have tracked message instances; delivery state below is read from the
        // database after the raw UPDATE. Detach only delivery entries to avoid stale rows without
        // disturbing other units of work a caller may have in the same context.
        foreach (var entry in _dbContext.ChangeTracker.Entries<OutboxHandlerDelivery>().ToList())
        {
            entry.State = EntityState.Detached;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var claimedIds = await _dbContext.Database
            .SqlQuery<Guid>($"""
                UPDATE "OutboxHandlerDeliveries" AS d
                SET "Status" = {(int)OutboxHandlerDeliveryStatus.Processing},
                    "LockedUntilUtc" = {lockUntil}
                WHERE d."Id" IN (
                    SELECT o."Id"
                    FROM "OutboxHandlerDeliveries" AS o
                    WHERE o."Channel" = {(int)channel}
                      AND (
                          (o."Status" = {(int)OutboxHandlerDeliveryStatus.Pending} AND o."AvailableAtUtc" <= {now})
                          OR (o."Status" = {(int)OutboxHandlerDeliveryStatus.Processing} AND o."LockedUntilUtc" <= {now})
                      )
                      AND (
                          o."Sequence" = 0
                          OR o."AggregateId" IS NULL
                          OR NOT EXISTS (
                              SELECT 1
                              FROM "OutboxHandlerDeliveries" AS p
                              WHERE p."Channel" = o."Channel"
                                AND p."AggregateId" = o."AggregateId"
                                AND p."Sequence" > 0
                                AND p."Sequence" < o."Sequence"
                                AND (p."Status" = {(int)OutboxHandlerDeliveryStatus.Pending}
                                    OR p."Status" = {(int)OutboxHandlerDeliveryStatus.Processing})
                          )
                      )
                    ORDER BY o."AvailableAtUtc", o."CreatedAtUtc"
                    FOR UPDATE SKIP LOCKED
                    LIMIT {_options.BatchSize}
                )
                RETURNING d."Id" AS "Value"
                """)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (claimedIds.Count == 0)
        {
            return new List<OutboxHandlerDelivery>();
        }

        return await _dbContext.OutboxHandlerDeliveries
            .Where(x => claimedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
    }

    private async Task ProcessDeliveryAsync(
        OutboxHandlerDelivery delivery,
        OutboxChannel channel,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var message = await _dbContext.OutboxMessages
            .SingleOrDefaultAsync(x => x.Id == delivery.OutboxMessageId, cancellationToken);

        if (message is null)
        {
            delivery.AttemptCount += 1;
            delivery.Status = OutboxHandlerDeliveryStatus.Failed;
            delivery.LockedUntilUtc = null;
            delivery.LastError = OutboxErrorSanitizer.Sanitize(
                "Outbox message no longer exists.");
            OutboxMetrics.RecordFailed();
            _logger.LogError(
                "Outbox delivery references a missing message | DeliveryId={DeliveryId} | MessageId={MessageId} | HandlerKey={HandlerKey}",
                delivery.Id, delivery.OutboxMessageId, delivery.HandlerKey);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!_handlersByKey.TryGetValue(delivery.HandlerKey, out var entry)
            || entry.Descriptor.Channel != channel
            || !entry.Handler.SupportedTypes.Contains(message.Type, StringComparer.Ordinal))
        {
            delivery.AttemptCount += 1;
            delivery.Status = OutboxHandlerDeliveryStatus.Failed;
            delivery.LockedUntilUtc = null;
            delivery.LastError = OutboxErrorSanitizer.Sanitize(
                $"No handler '{delivery.HandlerKey}' registered for event type '{message.Type}' in channel '{channel}'.");
            OutboxMetrics.RecordFailed();
            _logger.LogError(
                "Outbox delivery has no matching handler | DeliveryId={DeliveryId} | MessageId={MessageId} | Type={Type} | HandlerKey={HandlerKey} | Channel={Channel}",
                delivery.Id, message.Id, message.Type, delivery.HandlerKey, channel);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await RecomputeMessageStatusAsync(message, cancellationToken);
            return;
        }

        var result = await InvokeHandlerAsync(entry.Handler, message, cancellationToken);

        stopwatch.Stop();
        OutboxMetrics.RecordProcessingDuration(stopwatch.Elapsed.TotalSeconds);

        var now = DateTime.UtcNow;
        delivery.LockedUntilUtc = null;

        if (result.Success)
        {
            delivery.Status = OutboxHandlerDeliveryStatus.Processed;
            delivery.ProcessedAtUtc = now;
            delivery.LastError = null;
            OutboxMetrics.RecordProcessed();
            _logger.LogInformation(
                "Outbox delivery processed | DeliveryId={DeliveryId} | MessageId={MessageId} | Type={Type} | HandlerKey={HandlerKey} | Channel={Channel} | Attempt={Attempt} | LatencyMs={LatencyMs}",
                delivery.Id, message.Id, message.Type, delivery.HandlerKey, channel, delivery.AttemptCount, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            delivery.AttemptCount += 1;

            if (!result.Retryable || delivery.AttemptCount >= _options.MaxAttempts)
            {
                delivery.Status = OutboxHandlerDeliveryStatus.Failed;
                delivery.LastError = OutboxErrorSanitizer.Sanitize(
                    result.Error,
                    entry.Handler.GetType().Name);
                OutboxMetrics.RecordFailed();
                _logger.LogError(
                    "Outbox delivery failed permanently | DeliveryId={DeliveryId} | MessageId={MessageId} | Type={Type} | HandlerKey={HandlerKey} | Channel={Channel} | Attempt={Attempt} | Error={Error}",
                    delivery.Id, message.Id, message.Type, delivery.HandlerKey, channel, delivery.AttemptCount, delivery.LastError);
            }
            else
            {
                delivery.Status = OutboxHandlerDeliveryStatus.Pending;
                delivery.AvailableAtUtc = now.Add(CalculateRetryDelay(delivery.AttemptCount));
                delivery.LastError = OutboxErrorSanitizer.Sanitize(
                    result.Error,
                    entry.Handler.GetType().Name);
                OutboxMetrics.RecordRetried();
                _logger.LogWarning(
                    "Outbox delivery scheduled for retry | DeliveryId={DeliveryId} | MessageId={MessageId} | Type={Type} | HandlerKey={HandlerKey} | Channel={Channel} | Attempt={Attempt} | NextAtUtc={NextAtUtc}",
                    delivery.Id, message.Id, message.Type, delivery.HandlerKey, channel, delivery.AttemptCount, delivery.AvailableAtUtc);
            }
        }

        // Persist the delivery on its own before projecting the message status. A failure in one
        // handler therefore never loses the outcome of a successful sibling delivery.
        await _dbContext.SaveChangesAsync(cancellationToken);

        await RecomputeMessageStatusAsync(message, cancellationToken);
    }

    /// <summary>
    /// Projects the per-delivery outcomes onto the legacy <see cref="OutboxMessage"/> status so the
    /// existing operations API and health check keep reporting meaningful totals. Deliveries remain
    /// the authoritative unit: message status never causes a delivery to run twice.
    /// </summary>
    private async Task RecomputeMessageStatusAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        var deliveries = await _dbContext.OutboxHandlerDeliveries
            .AsNoTracking()
            .Where(x => x.OutboxMessageId == message.Id)
            .Select(x => new
            {
                x.Status,
                x.AttemptCount,
                x.AvailableAtUtc,
                x.LastError
            })
            .ToListAsync(cancellationToken);

        if (deliveries.Count == 0)
        {
            return;
        }

        var hasProcessing = deliveries.Any(x => x.Status == OutboxHandlerDeliveryStatus.Processing);
        var hasPending = deliveries.Any(x => x.Status == OutboxHandlerDeliveryStatus.Pending);
        var failed = deliveries.FirstOrDefault(x => x.Status == OutboxHandlerDeliveryStatus.Failed);

        var status = hasProcessing
            ? OutboxMessageStatus.Processing
            : hasPending
                ? OutboxMessageStatus.Pending
                : failed is not null
                    ? OutboxMessageStatus.Failed
                    : OutboxMessageStatus.Processed;

        message.Status = status;
        message.AttemptCount = deliveries.Max(x => x.AttemptCount);
        message.LockedUntilUtc = null;

        if (status == OutboxMessageStatus.Processed)
        {
            message.ProcessedAtUtc ??= DateTime.UtcNow;
            message.LastError = null;
        }
        else
        {
            message.ProcessedAtUtc = null;
            message.LastError = failed?.LastError;

            if (hasPending)
            {
                message.AvailableAtUtc = deliveries
                    .Where(x => x.Status == OutboxHandlerDeliveryStatus.Pending)
                    .Min(x => x.AvailableAtUtc);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task BackfillDeliveriesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var limit = Math.Max(_options.BatchSize * 5, 20);

        if (_dbContext.Database.IsRelational())
        {
            await BackfillDeliveriesRelationalAsync(now, limit, cancellationToken);
            return;
        }

        await BackfillDeliveriesInMemoryAsync(now, limit, cancellationToken);
    }

    private async Task BackfillDeliveriesRelationalAsync(
        DateTime now,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Only non-terminal messages are considered. Processed/Failed legacy messages keep their
        // status and payload and are never replayed. Processing messages are only eligible after
        // their legacy lock expires, so an in-flight legacy worker is never raced.
        var candidateIds = await _dbContext.Database
            .SqlQuery<Guid>($"""
                SELECT m."Id" AS "Value"
                FROM "OutboxMessages" AS m
                WHERE m."Status" IN ({(int)OutboxMessageStatus.Pending}, {(int)OutboxMessageStatus.Processing})
                  AND m."AvailableAtUtc" <= {now}
                  AND (
                      m."Status" = {(int)OutboxMessageStatus.Pending}
                      OR m."LockedUntilUtc" IS NULL
                      OR m."LockedUntilUtc" <= {now}
                  )
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "OutboxHandlerDeliveries" AS d
                      WHERE d."OutboxMessageId" = m."Id"
                  )
                ORDER BY m."AvailableAtUtc", m."OccurredAtUtc"
                LIMIT {limit}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var messages = await _dbContext.OutboxMessages
            .Where(x => candidateIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            await FanOutMessageRelationalAsync(message, now, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FanOutMessageRelationalAsync(
        OutboxMessage message,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var matching = MatchingHandlers(message);

        if (matching.Count == 0)
        {
            MarkUnhandled(message);
            return;
        }

        foreach (var entry in matching)
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "OutboxHandlerDeliveries"
                    ("Id", "OutboxMessageId", "HandlerKey", "Channel", "AggregateId", "Sequence",
                     "Status", "AttemptCount", "AvailableAtUtc", "LockedUntilUtc", "ProcessedAtUtc",
                     "LastError", "CreatedAtUtc")
                VALUES
                    ({Guid.CreateVersion7()}, {message.Id}, {entry.Descriptor.HandlerKey},
                     {(int)entry.Descriptor.Channel}, {message.AggregateId}, {message.Sequence},
                     {(int)OutboxHandlerDeliveryStatus.Pending}, {0}, {message.AvailableAtUtc},
                     {(DateTime?)null}, {(DateTime?)null}, {(string?)null}, {now})
                ON CONFLICT ("OutboxMessageId", "HandlerKey") DO NOTHING
                """, cancellationToken);
        }
    }

    private async Task BackfillDeliveriesInMemoryAsync(
        DateTime now,
        int limit,
        CancellationToken cancellationToken)
    {
        var messages = await _dbContext.OutboxMessages
            .Where(x => (x.Status == OutboxMessageStatus.Pending
                    || (x.Status == OutboxMessageStatus.Processing
                        && (x.LockedUntilUtc == null || x.LockedUntilUtc <= now)))
                && x.AvailableAtUtc <= now
                && !_dbContext.OutboxHandlerDeliveries.Any(d => d.OutboxMessageId == x.Id))
            .OrderBy(x => x.AvailableAtUtc)
            .ThenBy(x => x.OccurredAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            await FanOutMessageInMemoryAsync(message, now, cancellationToken);
        }
    }

    private async Task FanOutMessageInMemoryAsync(
        OutboxMessage message,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var matching = MatchingHandlers(message);

        if (matching.Count == 0)
        {
            MarkUnhandled(message);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var existingKeys = await _dbContext.OutboxHandlerDeliveries
            .Where(x => x.OutboxMessageId == message.Id)
            .Select(x => x.HandlerKey)
            .ToListAsync(cancellationToken);

        foreach (var entry in matching)
        {
            if (existingKeys.Contains(entry.Descriptor.HandlerKey, StringComparer.Ordinal))
            {
                continue;
            }

            _dbContext.OutboxHandlerDeliveries.Add(new OutboxHandlerDelivery
            {
                OutboxMessageId = message.Id,
                HandlerKey = entry.Descriptor.HandlerKey,
                Channel = entry.Descriptor.Channel,
                AggregateId = message.AggregateId,
                Sequence = message.Sequence,
                Status = OutboxHandlerDeliveryStatus.Pending,
                AttemptCount = 0,
                AvailableAtUtc = message.AvailableAtUtc,
                CreatedAtUtc = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void MarkUnhandled(OutboxMessage message)
    {
        message.AttemptCount += 1;
        message.Status = OutboxMessageStatus.Failed;
        message.LockedUntilUtc = null;
        message.LastError = OutboxErrorSanitizer.Sanitize(
            $"No handler registered for event type '{message.Type}'.");
        OutboxMetrics.RecordFailed();
        _logger.LogError(
            "Outbox message has no handler | MessageId={MessageId} | Type={Type} | AggregateId={AggregateId}",
            message.Id, message.Type, message.AggregateId);
    }

    private IReadOnlyList<HandlerEntry> MatchingHandlers(OutboxMessage message)
    {
        return _handlers
            .Where(x => x.Handler.SupportedTypes.Contains(message.Type, StringComparer.Ordinal))
            .ToList();
    }

    private async Task<OutboxProcessingResult> InvokeHandlerAsync(
        IOutboxEventHandler handler,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            return await handler.HandleAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            return OutboxProcessingResult.Retry(exception.Message);
        }
    }

    private TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var exponential = _options.RetryBaseDelay.TotalSeconds * Math.Pow(2, Math.Min(attemptCount - 1, 6));
        var jitter = Random.Shared.NextDouble() * 0.5 + 0.75;
        var seconds = Math.Min(exponential * jitter, TimeSpan.FromHours(1).TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private sealed record HandlerEntry(
        IOutboxEventHandler Handler,
        OutboxHandlerDescriptor Descriptor);
}
