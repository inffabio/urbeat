using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Urbeat.Application.Analytics;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class OrderReportServiceTests : IDisposable
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DefaultDay = new(2026, 10, 29);

    private readonly ApplicationDbContext _db;
    private readonly FixedTimeProvider _timeProvider;
    private readonly OrderReportService _sut;
    private int _claimId;

    public OrderReportServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-order-report-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _timeProvider = new FixedTimeProvider(DefaultNow);
        _sut = new OrderReportService(_db, _timeProvider);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task GetStoreSimpleReportAsync_ShouldCountOperationalOrdersInProgressForSellerStoreOnly()
    {
        var sellerUserId = Guid.NewGuid();
        var otherSellerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja", Slug = "loja", PhoneNumber = "11999999999" };
        var otherStore = new Store { OwnerUserId = otherSellerUserId, Name = "Outra", Slug = "outra", PhoneNumber = "11888888888" };
        _db.Stores.AddRange(store, otherStore);
        _db.Orders.AddRange(
            CreateOrder(store.Id, OrderStatus.Received),
            CreateOrder(store.Id, OrderStatus.Preparing),
            CreateOrder(store.Id, OrderStatus.Ready),
            CreateOrder(store.Id, OrderStatus.OnDelivery),
            CreateOrder(store.Id, OrderStatus.Delivered),
            CreateOrder(store.Id, OrderStatus.Cancelled),
            CreateOrder(store.Id, OrderStatus.PendingPayment),
            CreateOrder(otherStore.Id, OrderStatus.Received));
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreSimpleReportAsync(sellerUserId, null, null);

        result.InProgressOrders.Should().Be(4);
    }

    // ---------- Analytics ----------

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldAggregateOnlyTheSellerStores()
    {
        var sellerUserId = Guid.NewGuid();
        var otherSellerUserId = Guid.NewGuid();
        var ownStore = AddStore(sellerUserId, "loja-propria");
        var otherStore = AddStore(otherSellerUserId, "loja-alheia");
        AddOrder(ownStore.Id, OrderStatus.Delivered, At(DefaultDay, 15), subtotal: 10m, deliveryFee: 0m);
        AddOrder(otherStore.Id, OrderStatus.Delivered, At(DefaultDay, 16), subtotal: 99m, deliveryFee: 0m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Kpis.DeliveredOrders.Should().Be(1);
        result.Kpis.Revenue.Should().Be(10m);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldOnlyConsiderDeliveredOrders()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-status");
        var statuses = new[]
        {
            OrderStatus.Created, OrderStatus.PendingPayment, OrderStatus.Received, OrderStatus.Preparing,
            OrderStatus.Ready, OrderStatus.OnDelivery, OrderStatus.Cancelled
        };
        foreach (var status in statuses)
        {
            AddOrder(store.Id, status, At(DefaultDay, 15));
        }
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 16));
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Kpis.DeliveredOrders.Should().Be(1);
        result.Payments.Should().ContainSingle();
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldUseInclusiveStartAndExclusiveEnd()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-limites");
        var range = DayRange(DefaultDay);
        AddOrder(store.Id, OrderStatus.Delivered, range.StartUtc, subtotal: 10m, deliveryFee: 0m);
        AddOrder(store.Id, OrderStatus.Delivered, range.EndUtcExclusive, subtotal: 20m, deliveryFee: 0m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, range);

        result.Kpis.DeliveredOrders.Should().Be(1);
        result.Kpis.Revenue.Should().Be(10m);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldIncludeDeliveryFeeInRevenue_AndReportItSeparately()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-taxas");
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 15), subtotal: 80m, deliveryFee: 5m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Kpis.Revenue.Should().Be(85m);
        result.Kpis.DeliveryFees.Should().Be(5m);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldCalculateAverageTicketAwayFromZero()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-ticket");
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 13), subtotal: 10m, deliveryFee: 0m);
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 14), subtotal: 10m, deliveryFee: 0m);
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 15), subtotal: 10m, deliveryFee: 5m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Kpis.Revenue.Should().Be(35m);
        result.Kpis.AverageTicket.Should().Be(11.67m);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldReturnZeroAverageTicket_WhenThereAreNoDeliveredOrders()
    {
        var sellerUserId = Guid.NewGuid();
        AddStore(sellerUserId, "loja-vazia");
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Kpis.DeliveredOrders.Should().Be(0);
        result.Kpis.Revenue.Should().Be(0m);
        result.Kpis.DeliveryFees.Should().Be(0m);
        result.Kpis.AverageTicket.Should().Be(0m);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldGroupPaymentsByMethod_OrderedByRevenueThenEnum()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-pagamentos");
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 11), 10m, 0m, PaymentMethod.PixOnline);
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 12), 30m, 0m, PaymentMethod.PixOnline);
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 13), 50m, 0m, PaymentMethod.CashOnDelivery);
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 14), 5m, 0m, PaymentMethod.CardOnDelivery);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Payments.Should().HaveCount(3);
        result.Payments.Select(x => x.PaymentMethod).Should().ContainInOrder(
            PaymentMethod.CashOnDelivery, PaymentMethod.PixOnline, PaymentMethod.CardOnDelivery);
        result.Payments.Single(x => x.PaymentMethod == PaymentMethod.PixOnline).DeliveredOrders.Should().Be(2);
        result.Payments.Single(x => x.PaymentMethod == PaymentMethod.PixOnline).Revenue.Should().Be(40m);
        result.Payments.Should().NotContain(x => x.PaymentMethod == PaymentMethod.CardOnline);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldGroupProductsByTrimmedSnapshotName()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-produtos");
        var order1 = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 11));
        var order2 = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 12));
        var cancelled = AddOrder(store.Id, OrderStatus.Cancelled, At(DefaultDay, 13));
        AddItem(order1.Id, " X-Burger ", quantity: 2, unitPrice: 10m);
        AddItem(order2.Id, "X-Burger", quantity: 3, unitPrice: 10m);
        AddItem(order2.Id, "x-burger", quantity: 1, unitPrice: 7m);
        AddItem(order2.Id, string.Empty, quantity: 4, unitPrice: 1m);
        AddItem(cancelled.Id, "X-Burger", quantity: 100, unitPrice: 10m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Products.Should().HaveCount(3);
        result.Products.Single(x => x.ProductName == "X-Burger").Quantity.Should().Be(5);
        result.Products.Single(x => x.ProductName == "x-burger").Quantity.Should().Be(1);
        result.Products.Single(x => x.ProductName == string.Empty).Quantity.Should().Be(4);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldOrderProductsByQuantityThenRevenueThenName()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-ranking");
        var order = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 11));
        AddItem(order.Id, "Zebra", quantity: 5, unitPrice: 10m);
        AddItem(order.Id, "Alpha", quantity: 5, unitPrice: 10m);
        AddItem(order.Id, "Beta", quantity: 5, unitPrice: 20m);
        AddItem(order.Id, "Gama", quantity: 1, unitPrice: 100m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Products.Select(x => x.ProductName).Should().ContainInOrder("Beta", "Alpha", "Zebra", "Gama");
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldZeroFillTwelveMonths_AndUseLocalCalendar()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-meses");
        // 2027-01-01T02:30Z is 2026-12-31 23:30 in Sao Paulo.
        AddOrder(store.Id, OrderStatus.Delivered, new DateTime(2027, 1, 1, 2, 30, 0, DateTimeKind.Utc));
        // 2026-01-01T02:30Z is 2025-12-31 23:30 in Sao Paulo.
        AddOrder(store.Id, OrderStatus.Delivered, new DateTime(2026, 1, 1, 2, 30, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(new DateOnly(2026, 12, 31)));

        result.Months.Should().HaveCount(12);
        result.Months.Select(x => x.Month).Should().Equal(Enumerable.Range(1, 12));
        result.Months.Single(x => x.Month == 12).DeliveredOrders.Should().Be(1);
        result.Months.Single(x => x.Month == 1).DeliveredOrders.Should().Be(0);
        result.Months.Where(x => x.Month != 12).Should().OnlyContain(x => x.DeliveredOrders == 0 && x.Revenue == 0m);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldTrimOnlyAsciiSpaces_ToMatchPostgresBtrim()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-normalizacao");
        var order = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 11));
        AddItem(order.Id, " X-Burger ", quantity: 1, unitPrice: 10m);
        AddItem(order.Id, "X-Burger", quantity: 1, unitPrice: 10m);
        AddItem(order.Id, "\tX-Burger", quantity: 1, unitPrice: 10m);
        AddItem(order.Id, "X-Burger\t", quantity: 1, unitPrice: 10m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        // PostgreSQL btrim removes ASCII spaces only; tabs are preserved. The in-memory
        // provider must follow the same documented semantic.
        result.Products.Should().ContainSingle(x => x.ProductName == "X-Burger" && x.Quantity == 2);
        result.Products.Should().ContainSingle(x => x.ProductName == "\tX-Burger" && x.Quantity == 1);
        result.Products.Should().ContainSingle(x => x.ProductName == "X-Burger\t" && x.Quantity == 1);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldGroupMonthsByLocalDstAwareBoundaries()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-meses-dst");
        // 2019-02-16 00:00 local is -02:00 (02:00Z); 2019-02-17 00:00 is -03:00 (03:00Z).
        AddOrder(store.Id, OrderStatus.Delivered, new DateTime(2019, 2, 16, 2, 0, 0, DateTimeKind.Utc));
        AddOrder(store.Id, OrderStatus.Delivered, new DateTime(2019, 2, 17, 3, 0, 0, DateTimeKind.Utc));
        // 2019-03-01 00:00 local is 03:00Z; 02:59Z is still February locally.
        AddOrder(store.Id, OrderStatus.Delivered, new DateTime(2019, 3, 1, 2, 59, 0, DateTimeKind.Utc));
        AddOrder(store.Id, OrderStatus.Delivered, new DateTime(2019, 3, 1, 3, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var range = new AnalyticsRange(
            AnalyticsGranularity.Day,
            new DateOnly(2019, 2, 16),
            null,
            SaoPauloCalendar.StartOfDayUtc(new DateOnly(2019, 2, 16)),
            SaoPauloCalendar.EndOfDayExclusiveUtc(new DateOnly(2019, 2, 16)),
            2019);

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, range);

        result.Months.Single(x => x.Month == 2).DeliveredOrders.Should().Be(3);
        result.Months.Single(x => x.Month == 3).DeliveredOrders.Should().Be(1);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldNotLimitAggregationsTo100Orders()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-105");
        for (var index = 0; index < 105; index++)
        {
            var order = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10).AddSeconds(index), 10m, 1m);
            AddItem(order.Id, "Item", 1, 10m);
        }
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.Kpis.DeliveredOrders.Should().Be(105);
        result.Kpis.Revenue.Should().Be(1155m);
        result.Kpis.DeliveryFees.Should().Be(105m);
        result.Products.Single().Quantity.Should().Be(105);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldReturnDescendingAvailableYearsIncludingCurrentYear()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-anos");
        AddOrder(store.Id, OrderStatus.Cancelled, new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.AvailableYears.Should().Equal(2026, 2025, 2024);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldSupportMaximumYearWithoutOverflow()
    {
        _timeProvider.UtcNow = new DateTimeOffset(9999, 12, 31, 12, 0, 0, TimeSpan.Zero);
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-9999");
        AddOrder(store.Id, OrderStatus.Delivered, new DateTime(9999, 12, 31, 15, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var range = DayRange(new DateOnly(9999, 12, 31));
        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, range);

        result.ChartYear.Should().Be(9999);
        result.Months.Should().HaveCount(12);
        result.Months.Single(x => x.Month == 12).DeliveredOrders.Should().Be(1);
        result.AvailableYears.Should().Equal(9999);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldReturnZeroedResponse_WhenSellerHasNoStore()
    {
        var result = await _sut.GetStoreAnalyticsAsync(Guid.NewGuid(), DayRange(DefaultDay));

        result.Kpis.DeliveredOrders.Should().Be(0);
        result.Products.Should().BeEmpty();
        result.Payments.Should().BeEmpty();
        result.Months.Should().HaveCount(12).And.OnlyContain(x => x.DeliveredOrders == 0 && x.Revenue == 0m);
        result.AvailableYears.Should().Equal(2026);
        result.Period.Should().Be("day");
        result.TimeZone.Should().Be(SaoPauloCalendar.TimeZoneId);
    }

    [Fact]
    public async Task GetStoreAnalyticsAsync_ShouldUseTimeProviderForCurrentYear()
    {
        _timeProvider.UtcNow = new DateTimeOffset(2030, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var sellerUserId = Guid.NewGuid();
        AddStore(sellerUserId, "loja-relogio");
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreAnalyticsAsync(sellerUserId, DayRange(DefaultDay));

        result.AvailableYears.Should().Equal(2030);
    }

    // ---------- Sales grid ----------

    [Fact]
    public async Task GetStoreSalesAsync_ShouldReturnFilterTotalsIndependentOfPage()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-totais");
        for (var index = 0; index < 45; index++)
        {
            AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10).AddSeconds(index), 10m, 2m);
        }
        await _db.SaveChangesAsync();

        var pageOne = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 1, 20);
        var pageTwo = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 2, 20);
        var pageThree = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 3, 20);

        pageOne.TotalItems.Should().Be(45);
        pageOne.TotalPages.Should().Be(3);
        pageTwo.Totals.Should().BeEquivalentTo(pageOne.Totals);
        pageThree.Totals.Should().BeEquivalentTo(pageOne.Totals);
        pageOne.Totals.SalesTotal.Should().Be(540m);
        pageOne.Totals.DeliveryFees.Should().Be(90m);
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldKeepTotals_WhenPageIsBeyondTheEnd()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-fim");
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10), 10m, 1m);
        await _db.SaveChangesAsync();

        var page = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 5, 20);

        page.Sales.Should().BeEmpty();
        page.TotalItems.Should().Be(1);
        page.TotalPages.Should().Be(1);
        page.Totals.SalesTotal.Should().Be(11m);
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldPaginateDeterministically_WhenTimestampsAreEqual()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-ordem");
        var createdAt = At(DefaultDay, 10);
        for (var index = 0; index < 30; index++)
        {
            AddOrder(store.Id, OrderStatus.Delivered, createdAt, 10m, 0m);
        }
        await _db.SaveChangesAsync();

        var pageOne = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 1, 20);
        var pageTwo = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 2, 20);

        var ids = pageOne.Sales.Select(x => x.OrderId).Concat(pageTwo.Sales.Select(x => x.OrderId)).ToList();
        ids.Should().HaveCount(30);
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldOnlyLoadItemsForTheCurrentPage()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-itens");
        var first = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10), 10m, 0m);
        var second = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 11), 20m, 0m);
        AddItem(first.Id, "Primeiro", 1, 10m);
        AddItem(second.Id, "Segundo", 2, 10m);
        await _db.SaveChangesAsync();

        var pageTwo = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 2, 1);

        pageTwo.Sales.Should().ContainSingle();
        pageTwo.Sales.Single().OrderId.Should().Be(first.Id);
        pageTwo.Sales.Single().Items.Select(x => x.ProductName).Should().Equal("Primeiro");
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldKeepSubtotalDeliveryFeeAndTotalIdentity()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-identidade");
        var paid = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10), 80m, 5m);
        var free = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 11), 30m, 0m);
        AddItem(paid.Id, "X-Burger", 2, 35m);
        AddItem(paid.Id, "Batata", 1, 10m);
        AddItem(free.Id, "Suco", 3, 10m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 1, 20);

        foreach (var sale in result.Sales)
        {
            sale.Items.Sum(x => x.TotalPrice).Should().Be(sale.Subtotal);
            (sale.Subtotal + sale.DeliveryFee).Should().Be(sale.Total);
        }
        result.Sales.Single(x => x.OrderId == free.Id).DeliveryFee.Should().Be(0m);
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldReadCustomerNameFromFullNameClaim()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-nomes");
        var withName = Guid.NewGuid();
        var withoutName = Guid.NewGuid();
        var duplicated = Guid.NewGuid();
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10), 10m, 0m, customerUserId: withName);
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 11), 10m, 0m, customerUserId: withoutName);
        AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 12), 10m, 0m, customerUserId: duplicated);
        AddClaim(withName, "FullName", "Maria Silva");
        AddClaim(duplicated, "FullName", "Primeiro Nome");
        AddClaim(duplicated, "FullName", "Segundo Nome");
        AddClaim(withName, "PhoneNumber", "11999999999");
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 1, 20);

        result.Sales.Single(x => x.CustomerName == "Maria Silva").Should().NotBeNull();
        result.Sales.Single(x => x.CustomerName == "Primeiro Nome").Should().NotBeNull();
        result.Sales.Should().ContainSingle(x => x.CustomerName == null);
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldOnlyIncludeDeliveredOrdersWithinInclusiveExclusiveRange()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-status");
        var range = DayRange(DefaultDay);
        AddOrder(store.Id, OrderStatus.Delivered, range.StartUtc, 10m, 0m);
        AddOrder(store.Id, OrderStatus.Delivered, range.EndUtcExclusive, 10m, 0m);
        AddOrder(store.Id, OrderStatus.Cancelled, At(DefaultDay, 14), 10m, 0m);
        AddOrder(store.Id, OrderStatus.PendingPayment, At(DefaultDay, 15), 10m, 0m);
        AddOrder(store.Id, OrderStatus.Preparing, At(DefaultDay, 16), 10m, 0m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreSalesAsync(sellerUserId, range, 1, 20);

        result.TotalItems.Should().Be(1);
        result.Sales.Should().ContainSingle();
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldIsolateOtherSellersFromRowsItemsAndTotals()
    {
        var sellerUserId = Guid.NewGuid();
        var otherSellerUserId = Guid.NewGuid();
        var ownStore = AddStore(sellerUserId, "loja-grade-dono");
        var otherStore = AddStore(otherSellerUserId, "loja-grade-outro");
        var ownOrder = AddOrder(ownStore.Id, OrderStatus.Delivered, At(DefaultDay, 10), 10m, 1m);
        var otherOrder = AddOrder(otherStore.Id, OrderStatus.Delivered, At(DefaultDay, 11), 99m, 9m);
        AddItem(ownOrder.Id, "Meu item", 1, 10m);
        AddItem(otherOrder.Id, "Item alheio", 1, 99m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 1, 20);

        result.TotalItems.Should().Be(1);
        result.Totals.SalesTotal.Should().Be(11m);
        result.Sales.Should().ContainSingle();
        result.Sales.Single().Items.Should().ContainSingle(x => x.ProductName == "Meu item");
        result.Sales.SelectMany(x => x.Items).Should().NotContain(x => x.ProductName == "Item alheio");
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldNormalizePaginationParameters()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-normaliza");
        for (var index = 0; index < 3; index++)
        {
            AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10).AddMinutes(index), 10m, 0m);
        }
        await _db.SaveChangesAsync();

        var negativePage = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), -5, 0);
        var oversized = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 1, 500);

        negativePage.Page.Should().Be(1);
        negativePage.PageSize.Should().Be(20);
        oversized.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldMatchAnalyticsForTheSameRange()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-consistencia");
        for (var index = 0; index < 7; index++)
        {
            AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10).AddMinutes(index), 10m, 2m);
        }
        await _db.SaveChangesAsync();
        var range = DayRange(DefaultDay);

        var analytics = await _sut.GetStoreAnalyticsAsync(sellerUserId, range);
        var sales = await _sut.GetStoreSalesAsync(sellerUserId, range, 1, 20);

        sales.TotalItems.Should().Be(analytics.Kpis.DeliveredOrders);
        sales.Totals.SalesTotal.Should().Be(analytics.Kpis.Revenue);
        sales.Totals.DeliveryFees.Should().Be(analytics.Kpis.DeliveryFees);
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldReturnEmptyGrid_WhenThereAreNoDeliveredOrders()
    {
        var sellerUserId = Guid.NewGuid();
        AddStore(sellerUserId, "loja-grade-vazia");
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 1, 20);

        result.Sales.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.Totals.SalesTotal.Should().Be(0m);
        result.Totals.DeliveryFees.Should().Be(0m);
    }

    [Fact]
    public async Task GetStoreSalesAsync_ShouldOrderItemsByProductNameThenId_AndReturnEmptyItems()
    {
        var sellerUserId = Guid.NewGuid();
        var store = AddStore(sellerUserId, "loja-grade-itens-ordem");
        var order = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 10), 30m, 0m);
        var emptyOrder = AddOrder(store.Id, OrderStatus.Delivered, At(DefaultDay, 9), 0m, 0m);
        AddItem(order.Id, "Zebra", 1, 10m);
        AddItem(order.Id, "Alpha", 1, 10m);
        AddItem(order.Id, "Alpha", 1, 10m);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreSalesAsync(sellerUserId, DayRange(DefaultDay), 1, 20);

        result.Sales.Single(x => x.OrderId == order.Id).Items.Select(x => x.ProductName)
            .Should().ContainInOrder("Alpha", "Alpha", "Zebra");
        result.Sales.Single(x => x.OrderId == emptyOrder.Id).Items.Should().BeEmpty();
    }

    // ---------- helpers ----------

    private static AnalyticsRange DayRange(DateOnly date)
        => new(
            AnalyticsGranularity.Day,
            date,
            null,
            SaoPauloCalendar.StartOfDayUtc(date),
            SaoPauloCalendar.EndOfDayExclusiveUtc(date),
            date.Year);

    private static DateTime At(DateOnly date, int hour)
        => new(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Utc);

    private Store AddStore(Guid ownerUserId, string slug)
    {
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = slug,
            Slug = slug,
            PhoneNumber = "11999999999"
        };
        _db.Stores.Add(store);
        return store;
    }

    private Order AddOrder(
        Guid storeId,
        OrderStatus status,
        DateTime createdAtUtc,
        decimal subtotal = 10m,
        decimal deliveryFee = 0m,
        PaymentMethod paymentMethod = PaymentMethod.PixOnline,
        Guid? customerUserId = null)
    {
        var order = CreateOrder(storeId, status);
        order.PaymentMethod = paymentMethod;
        order.CustomerUserId = customerUserId ?? Guid.NewGuid();
        order.Subtotal = subtotal;
        order.DeliveryFee = deliveryFee;
        order.Total = subtotal + deliveryFee;
        SetCreatedAtUtc(order, createdAtUtc);
        _db.Orders.Add(order);
        return order;
    }

    private OrderItem AddItem(Guid orderId, string productName, int quantity, decimal unitPrice, string? variationName = null)
    {
        var item = new OrderItem
        {
            OrderId = orderId,
            ProductName = productName,
            VariationName = variationName,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TotalPrice = quantity * unitPrice
        };
        _db.OrderItems.Add(item);
        return item;
    }

    private void AddClaim(Guid userId, string claimType, string claimValue)
    {
        _db.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            Id = ++_claimId,
            UserId = userId,
            ClaimType = claimType,
            ClaimValue = claimValue
        });
    }

    private static Order CreateOrder(Guid storeId, OrderStatus status)
    {
        return new Order
        {
            Code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            CustomerUserId = Guid.NewGuid(),
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Status = status,
            Subtotal = 10m,
            Total = 10m
        };
    }

    private static void SetCreatedAtUtc(BaseEntity entity, DateTime value)
        => typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.CreatedAtUtc))!
            .SetValue(entity, DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class FixedTimeProvider : TimeProvider
    {
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
