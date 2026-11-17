using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class DeliveryDriverServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public DeliveryDriverServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-delivery-drivers-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task AssignAsync_ShouldRejectPickupOrder_WithoutCreatingAssignment()
    {
        var sellerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = sellerUserId,
            Name = "Loja Teste",
            Slug = "loja-teste-driver-pickup",
            PhoneNumber = "11999999999"
        };
        var order = new Order
        {
            Code = "URB-PICKUP-3",
            CustomerUserId = Guid.NewGuid(),
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.PickUp,
            Status = OrderStatus.Ready,
            Total = 20m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var sut = new DeliveryDriverService(_db, new EfUnitOfWork(_db), Mock.Of<IOrderService>());

        var result = await sut.AssignAsync(
            sellerUserId,
            order.Id,
            Guid.NewGuid(),
            "127.0.0.1");

        result.InvalidState.Should().BeTrue();
        (await _db.OrderDeliveryAssignments.CountAsync()).Should().Be(0);
        (await _db.AuditLogs.CountAsync()).Should().Be(0);
    }
}
