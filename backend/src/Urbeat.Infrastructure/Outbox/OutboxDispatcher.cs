using System.Diagnostics;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Outbox;

public sealed class OutboxDispatcher : IOutboxDispatcher
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEnumerable<IOutboxEventHandler> _handlers;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        ApplicationDbContext dbContext,
        IEnumerable<IOutboxEventHandler> handlers,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _dbContext = dbContext;
        _handlers = handlers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var claimed = await ClaimBatchAsync(now, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        foreach (var message in claimed)
        {
            await ProcessMessageAsync(message, now, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return claimed.Count;
    }

    private async Task<List<OutboxMessage>> ClaimBatchAsync(DateTime now, CancellationToken cancellationToken)
    {
        var lockUntil = now.Add(_options.LockDuration);

        if (_dbContext.Database.IsRelational())
        {
            return await ClaimBatchRelationalAsync(now, lockUntil, cancellationToken);
        }

        return await ClaimBatchInMemoryAsync(now, lockUntil, cancellationToken);
    }

    private async Task<List<OutboxMessage>> ClaimBatchInMemoryAsync(DateTime now, DateTime lockUntil, CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.OutboxMessages
            .Where(x => (x.Status == OutboxMessageStatus.Pending && x.AvailableAtUtc <= now)
                || (x.Status == OutboxMessageStatus.Processing && x.LockedUntilUtc <= now))
            .OrderBy(x => x.AvailableAtUtc)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new List<OutboxMessage>();
        }

        // Per-aggregate ordering: an ordered (Sequence > 0) candidate is only eligible when no
        // earlier sequence for the same aggregate is still pending or processing.
        var activeSequences = await _dbContext.OutboxMessages
            .Where(x => x.Sequence > 0 && (x.Status == OutboxMessageStatus.Pending || x.Status == OutboxMessageStatus.Processing))
            .Select(x => new { x.AggregateId, x.Sequence })
            .ToListAsync(cancellationToken);

        var lowestActiveByAggregate = activeSequences
            .GroupBy(x => x.AggregateId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Sequence));

        var eligible = candidates
            .Where(x => x.Sequence == 0
                || !lowestActiveByAggregate.TryGetValue(x.AggregateId, out var lowest)
                || lowest >= x.Sequence)
            .Take(_options.BatchSize)
            .ToList();

        foreach (var candidate in eligible)
        {
            candidate.Status = OutboxMessageStatus.Processing;
            candidate.LockedUntilUtc = lockUntil;
        }

        if (eligible.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return eligible;
    }

    private async Task<List<OutboxMessage>> ClaimBatchRelationalAsync(DateTime now, DateTime lockUntil, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var claimedIds = await _dbContext.Database
            .SqlQuery<Guid>($"""
                UPDATE "OutboxMessages" AS m
                SET "Status" = {(int)OutboxMessageStatus.Processing},
                    "LockedUntilUtc" = {lockUntil}
                WHERE m."Id" IN (
                    SELECT o."Id"
                    FROM "OutboxMessages" AS o
                    WHERE (
                        (o."Status" = {(int)OutboxMessageStatus.Pending} AND o."AvailableAtUtc" <= {now})
                        OR (o."Status" = {(int)OutboxMessageStatus.Processing} AND o."LockedUntilUtc" <= {now})
                    )
                    AND (
                        o."Sequence" = 0
                        OR NOT EXISTS (
                            SELECT 1
                            FROM "OutboxMessages" AS p
                            WHERE p."AggregateId" = o."AggregateId"
                              AND p."Sequence" > 0
                              AND p."Sequence" < o."Sequence"
                              AND (p."Status" = {(int)OutboxMessageStatus.Pending} OR p."Status" = {(int)OutboxMessageStatus.Processing})
                        )
                    )
                    ORDER BY o."AvailableAtUtc"
                    FOR UPDATE SKIP LOCKED
                    LIMIT {_options.BatchSize}
                )
                RETURNING m."Id"
                """)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (claimedIds.Count == 0)
        {
            return new List<OutboxMessage>();
        }

        return await _dbContext.OutboxMessages
            .Where(x => claimedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
    }

    private async Task ProcessMessageAsync(OutboxMessage message, DateTime now, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var matchingHandlers = _handlers
            .Where(x => x.SupportedTypes.Contains(message.Type))
            .ToList();

        if (matchingHandlers.Count == 0)
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
            return;
        }

        var success = true;
        string? permanentError = null;
        string? permanentErrorHandlerType = null;
        string? retryError = null;
        string? retryErrorHandlerType = null;

        foreach (var handler in matchingHandlers)
        {
            var result = await InvokeHandlerAsync(handler, message, cancellationToken);
            if (result.Success)
            {
                continue;
            }

            success = false;
            if (!result.Retryable)
            {
                permanentError = result.Error;
                permanentErrorHandlerType = handler.GetType().Name;
                break;
            }

            retryError ??= result.Error;
            retryErrorHandlerType ??= handler.GetType().Name;
        }

        stopwatch.Stop();
        OutboxMetrics.RecordProcessingDuration(stopwatch.Elapsed.TotalSeconds);

        if (success)
        {
            message.Status = OutboxMessageStatus.Processed;
            message.ProcessedAtUtc = now;
            message.LockedUntilUtc = null;
            message.LastError = null;
            OutboxMetrics.RecordProcessed();
            _logger.LogInformation(
                "Outbox message processed | MessageId={MessageId} | Type={Type} | AggregateId={AggregateId} | Attempt={Attempt} | LatencyMs={LatencyMs}",
                message.Id, message.Type, message.AggregateId, message.AttemptCount, stopwatch.ElapsedMilliseconds);
            return;
        }

        message.AttemptCount += 1;

        if (permanentError is not null || message.AttemptCount >= _options.MaxAttempts)
        {
            message.Status = OutboxMessageStatus.Failed;
            message.LockedUntilUtc = null;
            message.LastError = OutboxErrorSanitizer.Sanitize(
                permanentError ?? retryError,
                permanentError is not null ? permanentErrorHandlerType : retryErrorHandlerType);
            OutboxMetrics.RecordFailed();
            _logger.LogError(
                "Outbox message failed permanently | MessageId={MessageId} | Type={Type} | AggregateId={AggregateId} | Attempt={Attempt} | Error={Error}",
                message.Id, message.Type, message.AggregateId, message.AttemptCount, message.LastError);
            return;
        }

        message.Status = OutboxMessageStatus.Pending;
        message.LockedUntilUtc = null;
        message.AvailableAtUtc = now.Add(CalculateRetryDelay(message.AttemptCount));
        OutboxMetrics.RecordRetried();
        _logger.LogWarning(
            "Outbox message scheduled for retry | MessageId={MessageId} | Type={Type} | AggregateId={AggregateId} | Attempt={Attempt} | NextAtUtc={NextAtUtc}",
            message.Id, message.Type, message.AggregateId, message.AttemptCount, message.AvailableAtUtc);
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
}
