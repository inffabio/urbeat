using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// Translation contracts for the PostgreSQL provider. These tests do not open a connection;
/// they prove that the expressions used by <c>OrderReportService</c>
/// produce the intended server-side SQL. Execution against PostgreSQL is covered by the
/// self-skipping relational tests when Docker is available.
/// </summary>
public sealed class OrderReportServiceSqlTranslationTests
{
    private static ApplicationDbContext CreateNpgsqlContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=translation;Username=translation;Password=translation")
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void ProductGrouping_ShouldUseSpaceOnlyBtrim()
    {
        using var db = CreateNpgsqlContext();

        var sql = db.OrderItems
            .AsNoTracking()
            .GroupBy(item => (item.ProductName ?? string.Empty).Trim(' '))
            .Select(group => new
            {
                ProductName = group.Key,
                Quantity = group.Sum(item => item.Quantity),
                Revenue = group.Sum(item => (decimal?)item.TotalPrice) ?? 0m
            })
            .ToQueryString();

        sql.Should().Contain("btrim");
        sql.Should().NotContain("regexp_replace");
    }

    [Fact]
    public void MonthlyConditionalAggregation_ShouldTranslateToServerSideCaseWhen()
    {
        using var db = CreateNpgsqlContext();
        var yearStart = SaoPauloCalendar.StartOfYearUtc(2026);
        var yearEnd = SaoPauloCalendar.EndOfYearExclusiveUtc(2026);
        var januaryStart = SaoPauloCalendar.StartOfMonthUtc(2026, 1);
        var januaryEnd = SaoPauloCalendar.EndOfMonthExclusiveUtc(2026, 1);

        var sql = db.Orders
            .AsNoTracking()
            .Where(order => order.Status == OrderStatus.Delivered
                && order.CreatedAtUtc >= yearStart
                && order.CreatedAtUtc < yearEnd)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                JanuaryCount = group.Count(order => order.CreatedAtUtc >= januaryStart && order.CreatedAtUtc < januaryEnd),
                JanuaryRevenue = group.Sum(order =>
                    (decimal?)(order.CreatedAtUtc >= januaryStart && order.CreatedAtUtc < januaryEnd ? order.Total : null)) ?? 0m
            })
            .ToQueryString();

        sql.Should().Contain("CASE");
        sql.Should().Contain("count(");
        sql.Should().Contain("sum(");
        sql.Should().Contain("GROUP BY");
    }
}
