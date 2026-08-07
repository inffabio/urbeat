using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class OrderReportService : IOrderReportService
{
    private readonly ApplicationDbContext _dbContext;

    public OrderReportService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StoreOrdersSimpleReportResponseDto> GetStoreSimpleReportAsync(
        Guid sellerUserId,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default)
    {
        var storeIds = await _dbContext.Stores
            .AsNoTracking()
            .Where(s => s.OwnerUserId == sellerUserId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var ordersQuery = _dbContext.Orders
            .AsNoTracking()
            .Where(o => storeIds.Contains(o.StoreId) && o.Status != OrderStatus.PendingPayment);

        if (startDateUtc.HasValue)
            ordersQuery = ordersQuery.Where(o => o.CreatedAtUtc >= startDateUtc.Value);
        if (endDateUtc.HasValue)
            ordersQuery = ordersQuery.Where(o => o.CreatedAtUtc <= endDateUtc.Value);

        var totalOrders = await ordersQuery.CountAsync(cancellationToken);
        var totalRevenue = await ordersQuery
            .Where(o => o.Status == OrderStatus.Delivered)
            .SumAsync(o => (decimal?)o.Total, cancellationToken) ?? 0m;
        var inProgressOrders = await ordersQuery
            .CountAsync(o =>
                o.Status == OrderStatus.Received ||
                o.Status == OrderStatus.Preparing ||
                o.Status == OrderStatus.Ready ||
                o.Status == OrderStatus.OnDelivery,
                cancellationToken);

        return new StoreOrdersSimpleReportResponseDto
        {
            TotalOrders = totalOrders,
            TotalRevenue = totalRevenue,
            InProgressOrders = inProgressOrders,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc
        };
    }
}
