using System.Data;
using Urbeat.Application.Analytics;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class OrderReportService : IOrderReportService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public OrderReportService(ApplicationDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
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

    public Task<StoreAnalyticsResponseDto> GetStoreAnalyticsAsync(
        Guid sellerUserId,
        AnalyticsRange range,
        CancellationToken cancellationToken = default)
        => ExecuteInReadSnapshotAsync(
            token => GetStoreAnalyticsCoreAsync(sellerUserId, range, token),
            cancellationToken);

    private async Task<StoreAnalyticsResponseDto> GetStoreAnalyticsCoreAsync(
        Guid sellerUserId,
        AnalyticsRange range,
        CancellationToken cancellationToken)
    {
        var storeIds = await LoadStoreIdsAsync(sellerUserId, cancellationToken);
        var currentYear = Math.Min(
            SaoPauloCalendar.GetLocalDate(_timeProvider.GetUtcNow()).Year,
            SaoPauloCalendar.MaxSupportedYear);

        if (storeIds.Count == 0)
        {
            return BuildEmptyAnalytics(range, currentYear);
        }

        var delivered = DeliveredOrdersInRange(storeIds, range);

        var kpis = await delivered
            .GroupBy(_ => 1)
            .Select(group => new
            {
                DeliveredOrders = group.Count(),
                Revenue = group.Sum(order => (decimal?)order.Total) ?? 0m,
                DeliveryFees = group.Sum(order => (decimal?)order.DeliveryFee) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);

        var deliveredOrders = kpis?.DeliveredOrders ?? 0;
        var revenue = kpis?.Revenue ?? 0m;
        var deliveryFees = kpis?.DeliveryFees ?? 0m;
        var averageTicket = deliveredOrders == 0
            ? 0m
            : Math.Round(revenue / deliveredOrders, 2, MidpointRounding.AwayFromZero);

        var payments = await delivered
            .GroupBy(order => order.PaymentMethod)
            .Select(group => new StoreAnalyticsPaymentDto
            {
                PaymentMethod = group.Key,
                DeliveredOrders = group.Count(),
                Revenue = group.Sum(order => (decimal?)order.Total) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var paymentRows = payments
            .OrderByDescending(item => item.Revenue)
            .ThenBy(item => (int)item.PaymentMethod)
            .ToList();

        var productRows = await delivered
            .Join(
                _dbContext.OrderItems.AsNoTracking(),
                order => order.Id,
                item => item.OrderId,
                (_, item) => item)
            // Deliberate cross-provider semantic: PostgreSQL translates Trim(' ') to btrim,
            // which removes ASCII spaces only. .NET Trim() would also remove tabs/newlines,
            // making InMemory and Npgsql group names differently.
            .GroupBy(item => (item.ProductName ?? string.Empty).Trim(' '))
            .Select(group => new
            {
                ProductName = group.Key,
                Quantity = group.Sum(item => item.Quantity),
                Revenue = group.Sum(item => (decimal?)item.TotalPrice) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var products = productRows
            .Select(row => new StoreAnalyticsProductDto
            {
                ProductName = row.ProductName,
                Quantity = row.Quantity,
                Revenue = row.Revenue
            })
            .OrderByDescending(item => item.Quantity)
            .ThenByDescending(item => item.Revenue)
            .ThenBy(item => item.ProductName, StringComparer.Ordinal)
            .ToList();

        // One server-side aggregation for the whole chart year. The UTC boundaries are
        // calculated per month with SaoPauloCalendar, so DST transitions are respected while
        // every predicate remains an explicit [start, end) comparison that EF can translate.
        var yearStart = SaoPauloCalendar.StartOfYearUtc(range.ChartYear);
        var yearEnd = SaoPauloCalendar.EndOfYearExclusiveUtc(range.ChartYear);
        var janStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 1);
        var janEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 1);
        var febStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 2);
        var febEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 2);
        var marStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 3);
        var marEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 3);
        var aprStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 4);
        var aprEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 4);
        var mayStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 5);
        var mayEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 5);
        var junStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 6);
        var junEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 6);
        var julStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 7);
        var julEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 7);
        var augStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 8);
        var augEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 8);
        var sepStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 9);
        var sepEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 9);
        var octStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 10);
        var octEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 10);
        var novStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 11);
        var novEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 11);
        var decStart = SaoPauloCalendar.StartOfMonthUtc(range.ChartYear, 12);
        var decEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(range.ChartYear, 12);

        var monthly = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => storeIds.Contains(order.StoreId)
                && order.Status == OrderStatus.Delivered
                && order.CreatedAtUtc >= yearStart
                && order.CreatedAtUtc < yearEnd)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                JanCount = group.Count(order => order.CreatedAtUtc >= janStart && order.CreatedAtUtc < janEnd),
                JanRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= janStart && order.CreatedAtUtc < janEnd ? order.Total : null)) ?? 0m,
                FebCount = group.Count(order => order.CreatedAtUtc >= febStart && order.CreatedAtUtc < febEnd),
                FebRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= febStart && order.CreatedAtUtc < febEnd ? order.Total : null)) ?? 0m,
                MarCount = group.Count(order => order.CreatedAtUtc >= marStart && order.CreatedAtUtc < marEnd),
                MarRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= marStart && order.CreatedAtUtc < marEnd ? order.Total : null)) ?? 0m,
                AprCount = group.Count(order => order.CreatedAtUtc >= aprStart && order.CreatedAtUtc < aprEnd),
                AprRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= aprStart && order.CreatedAtUtc < aprEnd ? order.Total : null)) ?? 0m,
                MayCount = group.Count(order => order.CreatedAtUtc >= mayStart && order.CreatedAtUtc < mayEnd),
                MayRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= mayStart && order.CreatedAtUtc < mayEnd ? order.Total : null)) ?? 0m,
                JunCount = group.Count(order => order.CreatedAtUtc >= junStart && order.CreatedAtUtc < junEnd),
                JunRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= junStart && order.CreatedAtUtc < junEnd ? order.Total : null)) ?? 0m,
                JulCount = group.Count(order => order.CreatedAtUtc >= julStart && order.CreatedAtUtc < julEnd),
                JulRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= julStart && order.CreatedAtUtc < julEnd ? order.Total : null)) ?? 0m,
                AugCount = group.Count(order => order.CreatedAtUtc >= augStart && order.CreatedAtUtc < augEnd),
                AugRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= augStart && order.CreatedAtUtc < augEnd ? order.Total : null)) ?? 0m,
                SepCount = group.Count(order => order.CreatedAtUtc >= sepStart && order.CreatedAtUtc < sepEnd),
                SepRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= sepStart && order.CreatedAtUtc < sepEnd ? order.Total : null)) ?? 0m,
                OctCount = group.Count(order => order.CreatedAtUtc >= octStart && order.CreatedAtUtc < octEnd),
                OctRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= octStart && order.CreatedAtUtc < octEnd ? order.Total : null)) ?? 0m,
                NovCount = group.Count(order => order.CreatedAtUtc >= novStart && order.CreatedAtUtc < novEnd),
                NovRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= novStart && order.CreatedAtUtc < novEnd ? order.Total : null)) ?? 0m,
                DecCount = group.Count(order => order.CreatedAtUtc >= decStart && order.CreatedAtUtc < decEnd),
                DecRevenue = group.Sum(order => (decimal?)(order.CreatedAtUtc >= decStart && order.CreatedAtUtc < decEnd ? order.Total : null)) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);

        var monthValues = new (int DeliveredOrders, decimal Revenue)[]
        {
            (monthly?.JanCount ?? 0, monthly?.JanRevenue ?? 0m),
            (monthly?.FebCount ?? 0, monthly?.FebRevenue ?? 0m),
            (monthly?.MarCount ?? 0, monthly?.MarRevenue ?? 0m),
            (monthly?.AprCount ?? 0, monthly?.AprRevenue ?? 0m),
            (monthly?.MayCount ?? 0, monthly?.MayRevenue ?? 0m),
            (monthly?.JunCount ?? 0, monthly?.JunRevenue ?? 0m),
            (monthly?.JulCount ?? 0, monthly?.JulRevenue ?? 0m),
            (monthly?.AugCount ?? 0, monthly?.AugRevenue ?? 0m),
            (monthly?.SepCount ?? 0, monthly?.SepRevenue ?? 0m),
            (monthly?.OctCount ?? 0, monthly?.OctRevenue ?? 0m),
            (monthly?.NovCount ?? 0, monthly?.NovRevenue ?? 0m),
            (monthly?.DecCount ?? 0, monthly?.DecRevenue ?? 0m)
        };

        var months = monthValues
            .Select((value, index) => new StoreAnalyticsMonthDto
            {
                Year = range.ChartYear,
                Month = index + 1,
                DeliveredOrders = value.DeliveredOrders,
                Revenue = value.Revenue
            })
            .ToList();

        var availableYears = await LoadAvailableYearsAsync(storeIds, currentYear, cancellationToken);

        return new StoreAnalyticsResponseDto
        {
            Period = ToPeriodValue(range.Period),
            AnchorDate = range.AnchorDate,
            WeekBlock = range.WeekBlock,
            RangeStartUtc = range.StartUtc,
            RangeEndUtcExclusive = range.EndUtcExclusive,
            TimeZone = SaoPauloCalendar.TimeZoneId,
            ChartYear = range.ChartYear,
            Kpis = new StoreAnalyticsKpisDto
            {
                DeliveredOrders = deliveredOrders,
                Revenue = revenue,
                DeliveryFees = deliveryFees,
                AverageTicket = averageTicket
            },
            Products = products,
            Payments = paymentRows,
            Months = months,
            AvailableYears = availableYears
        };
    }

    public Task<PagedStoreSalesResponseDto> GetStoreSalesAsync(
        Guid sellerUserId,
        AnalyticsRange range,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => ExecuteInReadSnapshotAsync(
            token => GetStoreSalesCoreAsync(sellerUserId, range, page, pageSize, token),
            cancellationToken);

    private async Task<PagedStoreSalesResponseDto> GetStoreSalesCoreAsync(
        Guid sellerUserId,
        AnalyticsRange range,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var storeIds = await LoadStoreIdsAsync(sellerUserId, cancellationToken);

        if (storeIds.Count == 0)
        {
            return BuildEmptySales(range, normalizedPage, normalizedPageSize);
        }

        var delivered = DeliveredOrdersInRange(storeIds, range);

        var aggregate = await delivered
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalItems = group.Count(),
                SalesTotal = group.Sum(order => (decimal?)order.Total) ?? 0m,
                DeliveryFees = group.Sum(order => (decimal?)order.DeliveryFee) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);

        var totalItems = aggregate?.TotalItems ?? 0;
        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)normalizedPageSize);

        if (totalItems == 0 || normalizedPage > totalPages)
        {
            return BuildSalesResponse(
                range,
                normalizedPage,
                normalizedPageSize,
                totalItems,
                totalPages,
                aggregate?.SalesTotal ?? 0m,
                aggregate?.DeliveryFees ?? 0m,
                []);
        }

        var pageRows = await delivered
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(order => new
            {
                OrderId = order.Id,
                Code = order.Code,
                CustomerUserId = order.CustomerUserId,
                CreatedAtUtc = order.CreatedAtUtc,
                Subtotal = order.Subtotal,
                DeliveryFee = order.DeliveryFee,
                Total = order.Total
            })
            .ToListAsync(cancellationToken);

        if (pageRows.Count == 0)
        {
            return BuildSalesResponse(
                range,
                normalizedPage,
                normalizedPageSize,
                totalItems,
                totalPages,
                aggregate?.SalesTotal ?? 0m,
                aggregate?.DeliveryFees ?? 0m,
                []);
        }

        var pageOrderIds = pageRows.Select(row => row.OrderId).ToArray();
        var itemRows = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(item => pageOrderIds.Contains(item.OrderId))
            .OrderBy(item => item.ProductName)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                OrderId = item.OrderId,
                ProductName = item.ProductName,
                VariationName = item.VariationName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.TotalPrice
            })
            .ToListAsync(cancellationToken);

        var itemsByOrder = itemRows
            .GroupBy(item => item.OrderId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<StoreSaleItemDto>)group
                    .Select(item => new StoreSaleItemDto
                    {
                        ProductName = item.ProductName,
                        VariationName = item.VariationName,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        TotalPrice = item.TotalPrice
                    })
                    .ToList());

        var customerIds = pageRows
            .Select(row => row.CustomerUserId)
            .Distinct()
            .ToArray();

        var claimRows = await _dbContext.UserClaims
            .AsNoTracking()
            .Where(claim => customerIds.Contains(claim.UserId) && claim.ClaimType == "FullName")
            .OrderBy(claim => claim.Id)
            .Select(claim => new { claim.UserId, claim.ClaimValue })
            .ToListAsync(cancellationToken);

        var namesByUser = claimRows
            .GroupBy(claim => claim.UserId)
            .ToDictionary(group => group.Key, group => group.First().ClaimValue);

        var sales = pageRows
            .Select(row => new StoreSaleRowDto
            {
                OrderId = row.OrderId,
                Code = row.Code,
                CustomerName = namesByUser.GetValueOrDefault(row.CustomerUserId),
                CreatedAtUtc = DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
                Subtotal = row.Subtotal,
                DeliveryFee = row.DeliveryFee,
                Total = row.Total,
                Items = itemsByOrder.GetValueOrDefault(row.OrderId) ?? []
            })
            .ToList();

        return BuildSalesResponse(
            range,
            normalizedPage,
            normalizedPageSize,
            totalItems,
            totalPages,
            aggregate?.SalesTotal ?? 0m,
            aggregate?.DeliveryFees ?? 0m,
            sales);
    }

    /// <summary>
    /// Relational providers run every query of one dashboard request inside a RepeatableRead
    /// transaction, so KPIs, products, payments, months, totals, rows and items all observe the
    /// same snapshot even if orders change concurrently. The in-memory test provider has no
    /// transaction support and executes the same code without a transaction.
    /// </summary>
    private async Task<T> ExecuteInReadSnapshotAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<List<Guid>> LoadStoreIdsAsync(Guid sellerUserId, CancellationToken cancellationToken)
        => await _dbContext.Stores
            .AsNoTracking()
            .Where(store => store.OwnerUserId == sellerUserId)
            .Select(store => store.Id)
            .ToListAsync(cancellationToken);

    private IQueryable<Order> DeliveredOrdersInRange(IReadOnlyCollection<Guid> storeIds, AnalyticsRange range)
        => _dbContext.Orders
            .AsNoTracking()
            .Where(order => storeIds.Contains(order.StoreId)
                && order.Status == OrderStatus.Delivered
                && order.CreatedAtUtc >= range.StartUtc
                && order.CreatedAtUtc < range.EndUtcExclusive);

    private async Task<IReadOnlyCollection<int>> LoadAvailableYearsAsync(
        IReadOnlyCollection<Guid> storeIds,
        int currentYear,
        CancellationToken cancellationToken)
    {
        var oldestUtc = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => storeIds.Contains(order.StoreId))
            .MinAsync(order => (DateTime?)order.CreatedAtUtc, cancellationToken);

        if (oldestUtc is null)
        {
            return [currentYear];
        }

        var oldestYear = SaoPauloCalendar.GetLocalDate(oldestUtc.Value).Year;
        if (oldestYear > currentYear)
        {
            return [currentYear];
        }

        return Enumerable.Range(oldestYear, currentYear - oldestYear + 1)
            .Reverse()
            .ToArray();
    }

    private static StoreAnalyticsResponseDto BuildEmptyAnalytics(AnalyticsRange range, int currentYear)
        => new()
        {
            Period = ToPeriodValue(range.Period),
            AnchorDate = range.AnchorDate,
            WeekBlock = range.WeekBlock,
            RangeStartUtc = range.StartUtc,
            RangeEndUtcExclusive = range.EndUtcExclusive,
            TimeZone = SaoPauloCalendar.TimeZoneId,
            ChartYear = range.ChartYear,
            Kpis = new StoreAnalyticsKpisDto(),
            Products = [],
            Payments = [],
            Months = BuildZeroMonths(range.ChartYear),
            AvailableYears = [currentYear]
        };

    private static IReadOnlyCollection<StoreAnalyticsMonthDto> BuildZeroMonths(int chartYear)
        => Enumerable.Range(1, 12)
            .Select(month => new StoreAnalyticsMonthDto { Year = chartYear, Month = month })
            .ToList();

    private static PagedStoreSalesResponseDto BuildEmptySales(AnalyticsRange range, int page, int pageSize)
        => BuildSalesResponse(range, page, pageSize, 0, 0, 0m, 0m, []);

    private static PagedStoreSalesResponseDto BuildSalesResponse(
        AnalyticsRange range,
        int page,
        int pageSize,
        int totalItems,
        int totalPages,
        decimal salesTotal,
        decimal deliveryFees,
        IReadOnlyCollection<StoreSaleRowDto> sales)
        => new()
        {
            Period = ToPeriodValue(range.Period),
            AnchorDate = range.AnchorDate,
            WeekBlock = range.WeekBlock,
            RangeStartUtc = range.StartUtc,
            RangeEndUtcExclusive = range.EndUtcExclusive,
            TimeZone = SaoPauloCalendar.TimeZoneId,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Totals = new StoreSalesTotalsDto
            {
                SalesTotal = salesTotal,
                DeliveryFees = deliveryFees
            },
            Sales = sales
        };

    private static string ToPeriodValue(AnalyticsGranularity period)
        => period.ToString().ToLowerInvariant();
}
