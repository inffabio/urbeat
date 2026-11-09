using Urbeat.Application.Security;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/outbox")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class OutboxOperationsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public OutboxOperationsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        // Message-level projection: kept for compatibility with existing operational dashboards.
        var pending = await _dbContext.OutboxMessages.CountAsync(x => x.Status == OutboxMessageStatus.Pending, cancellationToken);
        var processing = await _dbContext.OutboxMessages.CountAsync(x => x.Status == OutboxMessageStatus.Processing, cancellationToken);
        var processed = await _dbContext.OutboxMessages.CountAsync(x => x.Status == OutboxMessageStatus.Processed, cancellationToken);
        var failed = await _dbContext.OutboxMessages.CountAsync(x => x.Status == OutboxMessageStatus.Failed, cancellationToken);

        // Handler deliveries are the authoritative unit. Reporting them per channel makes a stuck
        // communications worker visible without hiding business progress (and vice versa).
        var deliveryCounts = await _dbContext.OutboxHandlerDeliveries
            .GroupBy(x => new { x.Channel, x.Status })
            .Select(group => new DeliveryCount
            {
                Channel = group.Key.Channel,
                Status = group.Key.Status,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var deliveries = new
        {
            business = BuildChannelSummary(deliveryCounts, OutboxChannel.Business),
            communications = BuildChannelSummary(deliveryCounts, OutboxChannel.Communications)
        };

        return Ok(new { pending, processing, processed, failed, deliveries });
    }

    [HttpGet("failed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFailed([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var query = _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(x => x.Status == OutboxMessageStatus.Failed)
            .OrderByDescending(x => x.OccurredAtUtc);

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)safePageSize);

        var rows = await query
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(x => new
            {
                x.Id,
                x.Type,
                x.AggregateId,
                x.AggregateType,
                x.OccurredAtUtc,
                x.AttemptCount,
                x.LastError
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new
        {
            x.Id,
            x.Type,
            x.AggregateId,
            x.AggregateType,
            x.OccurredAtUtc,
            x.AttemptCount,
            LastError = OutboxErrorSanitizer.Sanitize(x.LastError)
        }).ToList();

        return Ok(new { page = safePage, pageSize = safePageSize, totalItems, totalPages, items });
    }

    [HttpPost("{id}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Retry([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var message = await _dbContext.OutboxMessages.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (message is null)
        {
            return NotFound();
        }

        if (message.Status != OutboxMessageStatus.Failed)
        {
            return BadRequest(new { error = "Only failed messages can be retried." });
        }

        // Requeue only the failed handler deliveries. Siblings already processed stay processed, so
        // a retry never re-sends an e-mail or SignalR push that already succeeded.
        var failedDeliveries = await _dbContext.OutboxHandlerDeliveries
            .Where(x => x.OutboxMessageId == id && x.Status == OutboxHandlerDeliveryStatus.Failed)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var delivery in failedDeliveries)
        {
            delivery.Status = OutboxHandlerDeliveryStatus.Pending;
            delivery.AvailableAtUtc = now;
            delivery.LockedUntilUtc = null;
            delivery.ProcessedAtUtc = null;
            delivery.AttemptCount = 0;
            delivery.LastError = null;
        }

        message.Status = OutboxMessageStatus.Pending;
        message.AvailableAtUtc = now;
        message.ProcessedAtUtc = null;
        message.AttemptCount = 0;
        message.LockedUntilUtc = null;
        message.LastError = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static object BuildChannelSummary(
        IReadOnlyCollection<DeliveryCount> counts,
        OutboxChannel channel)
    {
        int CountOf(OutboxHandlerDeliveryStatus status) =>
            counts
                .Where(x => x.Channel == channel && x.Status == status)
                .Sum(x => x.Count);

        return new
        {
            pending = CountOf(OutboxHandlerDeliveryStatus.Pending),
            processing = CountOf(OutboxHandlerDeliveryStatus.Processing),
            processed = CountOf(OutboxHandlerDeliveryStatus.Processed),
            failed = CountOf(OutboxHandlerDeliveryStatus.Failed)
        };
    }

    private sealed class DeliveryCount
    {
        public OutboxChannel Channel { get; init; }

        public OutboxHandlerDeliveryStatus Status { get; init; }

        public int Count { get; init; }
    }
}
