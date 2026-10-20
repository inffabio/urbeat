using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class OrderPrintingService : IOrderPrintingService
{
    private const int MaxLimit = 100;
    private const int MaxErrorLength = 500;
    private const int MaxOperationKeyLength = 320;

    private readonly ApplicationDbContext _dbContext;

    public OrderPrintingService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PendingPrintJobsResponseDto> ListPendingAsync(
        Guid sellerUserId,
        long afterCursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeCursor = Math.Max(0, afterCursor);
        var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, MaxLimit);
        var ownedStoreIds = _dbContext.Stores
            .AsNoTracking()
            .Where(store => store.OwnerUserId == sellerUserId)
            .Select(store => store.Id);

        var rows = await _dbContext.OrderPrintJobs
            .AsNoTracking()
            .Where(job => ownedStoreIds.Contains(job.StoreId)
                && (job.Status == OrderPrintJobStatus.Pending || job.Status == OrderPrintJobStatus.Ambiguous)
                && job.Cursor > safeCursor)
            .OrderBy(job => job.Cursor)
            .Take(safeLimit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > safeLimit;
        var page = rows.Take(safeLimit).ToList();

        return new PendingPrintJobsResponseDto
        {
            Items = page.Select(ToDto).ToList(),
            NextCursor = page.Count > 0 ? page[^1].Cursor : safeCursor,
            HasMore = hasMore
        };
    }

    public async Task<RecordPrintJobResultResponseDto> RecordResultAsync(
        Guid sellerUserId,
        Guid orderId,
        RecordPrintJobResultRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.OrderPrintJobs
            .Where(candidate => candidate.OrderId == orderId)
            .Join(
                _dbContext.Stores.Where(store => store.OwnerUserId == sellerUserId),
                candidate => candidate.StoreId,
                store => store.Id,
                (candidate, _) => candidate)
            .SingleOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return new RecordPrintJobResultResponseDto { NotFound = true };
        }

        var now = DateTime.UtcNow;
        job.AttemptCount += 1;
        job.LastAttemptAtUtc = now;
        job.LastError = NormalizeError(request.Message);
        job.LastOperationKey = NormalizeOperationKey(request.OperationKey);
        job.UpdatedAtUtc = now;

        if (request.Success)
        {
            job.Status = OrderPrintJobStatus.Completed;
            job.CompletedAtUtc = now;
            job.LastError = null;
        }
        else if (request.Ambiguous)
        {
            // The agent crashed around the spooler call. Never auto-retry: physical output is
            // unknown. The dashboard shows the state and the agent requires an explicit retry.
            job.Status = OrderPrintJobStatus.Ambiguous;
        }
        else
        {
            // Keep the job Pending so the next dashboard load/reconnect can retry it. The local
            // agent's operation key prevents a duplicate when the first attempt actually printed.
            job.Status = OrderPrintJobStatus.Pending;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RecordPrintJobResultResponseDto { Job = ToDto(job) };
    }

    private static PendingPrintJobDto ToDto(OrderPrintJob job) => new()
    {
        Cursor = job.Cursor,
        OrderId = job.OrderId,
        StoreId = job.StoreId,
        OrderCode = job.Code,
        AcceptanceSequence = job.AcceptanceSequence,
        AcceptedAtUtc = job.AcceptedAtUtc,
        Status = job.Status switch
        {
            OrderPrintJobStatus.Ambiguous => "ambiguous",
            OrderPrintJobStatus.Completed => "completed",
            _ => "pending"
        },
        AttemptCount = job.AttemptCount,
        LastError = job.LastError
    };

    private static string? NormalizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;
        var trimmed = error.Trim();
        return trimmed.Length <= MaxErrorLength ? trimmed : trimmed[..MaxErrorLength];
    }

    private static string? NormalizeOperationKey(string? operationKey)
    {
        if (string.IsNullOrWhiteSpace(operationKey)) return null;
        var trimmed = operationKey.Trim();
        return trimmed.Length <= MaxOperationKeyLength ? trimmed : trimmed[..MaxOperationKeyLength];
    }
}
