using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class ReviewService : IReviewService
{
    private readonly ApplicationDbContext _dbContext;

    public ReviewService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReviewResponseDto> CreateOrUpdateAsync(
        Guid customerUserId,
        Guid orderId,
        CreateReviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.Rating < 1 || request.Rating > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Rating), "Rating must be between 1 and 5.");
        }

        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(x => x.Id == orderId && x.CustomerUserId == customerUserId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

        if (order.Status != OrderStatus.Delivered)
        {
            throw new InvalidOperationException("Only delivered orders can be reviewed.");
        }

        var existing = await _dbContext.Set<OrderReview>()
            .FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

        if (existing is not null)
        {
            existing.Rating = request.Rating;
            existing.Comment = request.Comment;
            existing.MarkAsUpdated();
        }
        else
        {
            existing = new OrderReview
            {
                OrderId = orderId,
                StoreId = order.StoreId,
                CustomerUserId = customerUserId,
                Rating = request.Rating,
                Comment = request.Comment,
            };
            await _dbContext.Set<OrderReview>().AddAsync(existing, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await RecalculateStoreAverageAsync(order.StoreId, cancellationToken);

        return new ReviewResponseDto
        {
            Id = existing.Id,
            OrderId = existing.OrderId,
            Rating = existing.Rating,
            Comment = existing.Comment,
            CreatedAtUtc = existing.CreatedAtUtc,
        };
    }

    public async Task<ReviewResponseDto?> GetByOrderAsync(
        Guid customerUserId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var review = await _dbContext.Set<OrderReview>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.CustomerUserId == customerUserId, cancellationToken);

        return review is null ? null : new ReviewResponseDto
        {
            Id = review.Id,
            OrderId = review.OrderId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAtUtc = review.CreatedAtUtc,
        };
    }

    public async Task<IReadOnlyCollection<StoreReviewResponseDto>> ListByStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<OrderReview>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new StoreReviewResponseDto
            {
                Id = x.Id,
                CustomerUserId = x.CustomerUserId,
                Rating = x.Rating,
                Comment = x.Comment,
                CreatedAtUtc = x.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<StoreReviewResponseDto>> ListBySellerAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<OrderReview>()
            .AsNoTracking()
            .Join(
                _dbContext.Stores.AsNoTracking().Where(x => x.OwnerUserId == sellerUserId),
                review => review.StoreId,
                store => store.Id,
                (review, _) => review)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new StoreReviewResponseDto
            {
                Id = x.Id,
                CustomerUserId = x.CustomerUserId,
                Rating = x.Rating,
                Comment = x.Comment,
                CreatedAtUtc = x.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);
    }

    private async Task RecalculateStoreAverageAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var stats = await _dbContext.Set<OrderReview>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .GroupBy(x => x.StoreId)
            .Select(g => new
            {
                Average = (double)g.Average(x => x.Rating),
                Count = g.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var store = await _dbContext.Stores.FirstOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        if (store is not null)
        {
            store.AverageRating = stats?.Average ?? 0;
            store.TotalReviews = stats?.Count ?? 0;
        }
    }
}
