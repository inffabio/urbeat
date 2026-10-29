using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Urbeat.Application.Analytics;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// Executes the analytics and sales aggregations against PostgreSQL. This proves that the
/// RepeatableRead snapshot transaction is accepted by Npgsql and that product normalization uses
/// PostgreSQL's space-only btrim semantics. It self-skips when Docker is unavailable, like the
/// other relational tests in this repository.
/// </summary>
public sealed class OrderReportServiceRelationalTests
{
    private readonly ITestOutputHelper _output;

    public OrderReportServiceRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AnalyticsAndSales_ShouldRunConsistentlyOnPostgreSql()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;

            var ownerUserId = Guid.NewGuid();
            var day = new DateOnly(2026, 10, 29);
            var range = new AnalyticsRange(
                AnalyticsGranularity.Day,
                day,
                null,
                SaoPauloCalendar.StartOfDayUtc(day),
                SaoPauloCalendar.EndOfDayExclusiveUtc(day),
                day.Year);

            await using (var setup = new ApplicationDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var store = new Store
                {
                    OwnerUserId = ownerUserId,
                    Name = "Loja Relacional",
                    Slug = "loja-relacional",
                    PhoneNumber = "11999999999"
                };
                setup.Stores.Add(store);
                await setup.SaveChangesAsync();

                var order = new Order
                {
                    Code = "RELAC001",
                    CustomerUserId = Guid.NewGuid(),
                    StoreId = store.Id,
                    FulfillmentType = FulfillmentType.Delivery,
                    PaymentMethod = PaymentMethod.PixOnline,
                    Status = OrderStatus.Delivered,
                    Subtotal = 30m,
                    DeliveryFee = 3m,
                    Total = 33m
                };
                SetCreatedAtUtc(order, new DateTime(2026, 10, 29, 15, 0, 0, DateTimeKind.Utc));
                setup.Orders.Add(order);

                setup.OrderItems.AddRange(
                    new OrderItem
                    {
                        OrderId = order.Id,
                        ProductName = " X-Burger ",
                        Quantity = 1,
                        UnitPrice = 10m,
                        TotalPrice = 10m
                    },
                    new OrderItem
                    {
                        OrderId = order.Id,
                        ProductName = "X-Burger",
                        Quantity = 1,
                        UnitPrice = 10m,
                        TotalPrice = 10m
                    },
                    new OrderItem
                    {
                        OrderId = order.Id,
                        ProductName = "\tX-Burger",
                        Quantity = 1,
                        UnitPrice = 10m,
                        TotalPrice = 10m
                    });
                await setup.SaveChangesAsync();
            }

            await using var context = new ApplicationDbContext(options);
            var service = new OrderReportService(context, TimeProvider.System);

            var analytics = await service.GetStoreAnalyticsAsync(ownerUserId, range);
            var sales = await service.GetStoreSalesAsync(ownerUserId, range, page: 1, pageSize: 20);

            analytics.Kpis.DeliveredOrders.Should().Be(1);
            analytics.Kpis.Revenue.Should().Be(33m);
            analytics.Products.Should().ContainSingle(product => product.ProductName == "X-Burger" && product.Quantity == 2);
            analytics.Products.Should().ContainSingle(product => product.ProductName == "\tX-Burger" && product.Quantity == 1);
            analytics.Months.Single(month => month.Month == 10).DeliveredOrders.Should().Be(1);
            sales.TotalItems.Should().Be(analytics.Kpis.DeliveredOrders);
            sales.Totals.SalesTotal.Should().Be(analytics.Kpis.Revenue);
            sales.Totals.DeliveryFees.Should().Be(analytics.Kpis.DeliveryFees);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static void SetCreatedAtUtc(BaseEntity entity, DateTime value)
        => typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.CreatedAtUtc))!
            .SetValue(entity, DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }
}
