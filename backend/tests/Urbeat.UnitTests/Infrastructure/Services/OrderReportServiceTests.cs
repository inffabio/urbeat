using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class OrderReportServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly OrderReportService _sut;

    public OrderReportServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-order-report-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new OrderReportService(_db);
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

    private static Order CreateOrder(Guid storeId, OrderStatus status)
    {
        return new Order
        {
            Code = Guid.NewGuid().ToString("N")[..8],
            CustomerUserId = Guid.NewGuid(),
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Status = status,
            Subtotal = 10m,
            Total = 10m
        };
    }
}
