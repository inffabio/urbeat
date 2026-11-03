using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// The delivery PIN can be confirmed for an order the seller already marked as Delivered. That
/// confirmation changes customer-visible details (delivery confirmation) without a status
/// transition, so it must hint the customer tracking without fabricating history or outbox status
/// events. The regular OnDelivery -> Delivered transition keeps using the outbox only.
/// </summary>
public sealed class DeliveryDriverServicePinTrackingHintTests
{
    private const string Pin = "1234";

    [Fact]
    public async Task ConfirmPin_OnAnOrderAlreadyDelivered_ShouldHintCustomerTrackingExactlyOnce()
    {
        var scenario = await SeedScenarioAsync(OrderStatus.Delivered);
        using var db = scenario.CreateContext();
        var notification = new Mock<INotificationService>();
        notification
            .Setup(x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService(db, notification.Object);

        var result = await service.ConfirmDeliveryPinAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            Pin,
            ipAddress: null);

        result.Status.Should().Be(OrderStatus.Delivered);
        result.DeliveryConfirmedAtUtc.Should().NotBeNull();

        notification.Verify(
            x => x.NotifyCustomerOrderStatusUpdatedAsync(
                scenario.CustomerUserId,
                scenario.OrderId,
                It.IsAny<string>(),
                OrderStatus.Delivered,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        using (var verification = scenario.CreateContext())
        {
            (await verification.OrderStatusHistories.CountAsync(x => x.OrderId == scenario.OrderId))
                .Should().Be(0, "the status did not change");
            (await verification.OutboxMessages.CountAsync())
                .Should().Be(0, "a hint must never fake a status transition for the outbox");
        }

        // The confirmation is idempotent: a repeat must not spam the customer hub.
        var repeated = await service.ConfirmDeliveryPinAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            Pin,
            ipAddress: null);

        repeated.DeliveryConfirmedAtUtc.Should().Be(result.DeliveryConfirmedAtUtc);
        notification.Verify(
            x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConfirmPin_OnTheStatusTransition_ShouldKeepTheOutboxAndSkipTheDirectHint()
    {
        var scenario = await SeedScenarioAsync(OrderStatus.OnDelivery);
        using var db = scenario.CreateContext();
        var notification = new Mock<INotificationService>();
        notification
            .Setup(x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService(db, notification.Object);

        var result = await service.ConfirmDeliveryPinAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            Pin,
            ipAddress: null);

        result.Status.Should().Be(OrderStatus.Delivered);

        notification.Verify(
            x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        notification.Verify(
            x => x.NotifyCustomerOrderStatusChangedAsync(
                scenario.CustomerUserId,
                scenario.OrderId,
                OrderStatus.Delivered,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        using var verification = scenario.CreateContext();
        (await verification.OrderStatusHistories.CountAsync(x => x.OrderId == scenario.OrderId))
            .Should().Be(1);

        var outbox = await verification.OutboxMessages.AsNoTracking().SingleAsync();
        outbox.Type.Should().Be(OutboxEventTypes.OrderStatusChanged);
    }

    private static DeliveryDriverService CreateService(
        ApplicationDbContext dbContext,
        INotificationService notificationService)
    {
        var userStore = new Mock<IUserStore<IdentityUser<Guid>>>();
        var userManager = new Mock<UserManager<IdentityUser<Guid>>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var roleManager = new Mock<RoleManager<IdentityRole<Guid>>>(
            new Mock<IRoleStore<IdentityRole<Guid>>>().Object, null!, null!, null!, null!);

        return new DeliveryDriverService(
            dbContext,
            new EfUnitOfWork(dbContext),
            userManager.Object,
            roleManager.Object,
            Mock.Of<IEncryptionService>(),
            notificationService,
            new OutboxWriter(dbContext),
            Mock.Of<IOrderService>(),
            Options.Create(new EmailConfirmationOptions()),
            NullLogger<DeliveryDriverService>.Instance);
    }

    private static async Task<PinScenario> SeedScenarioAsync(OrderStatus status)
    {
        var scenario = new PinScenario($"urbeat-pin-hint-{Guid.NewGuid()}");

        var storeId = Guid.NewGuid();
        var driver = new DeliveryDriver
        {
            UserId = scenario.DriverUserId,
            FullName = "Entregador PIN",
            Email = "driver.pin@urbeat.local",
            NormalizedEmail = "DRIVER.PIN@URBEAT.LOCAL",
            PhoneNumber = "11999990002"
        };
        var order = new Order
        {
            Code = $"URB-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            CustomerUserId = scenario.CustomerUserId,
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.PixOnDelivery,
            Status = status,
            StatusVersion = status == OrderStatus.Delivered ? 3 : 2,
            Subtotal = 24m,
            DeliveryFee = 0m,
            Total = 24m,
            PaymentConfirmedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        order.DeliveryPinHash = new PasswordHasher<Order>().HashPassword(order, Pin);

        var link = new StoreDeliveryDriver
        {
            StoreId = storeId,
            DeliveryDriverId = driver.Id,
            Status = StoreDeliveryDriverStatus.Active,
            DisplayName = driver.FullName,
            PhoneNumber = driver.PhoneNumber,
            AcceptedAtUtc = DateTime.UtcNow
        };
        var assignment = new OrderDeliveryAssignment
        {
            OrderId = order.Id,
            StoreId = storeId,
            DeliveryDriverId = driver.Id,
            AssignedByUserId = Guid.NewGuid(),
            AssignedAtUtc = DateTime.UtcNow,
            DeliveryStartedAtUtc = DateTime.UtcNow,
            HandoverConfirmedAtUtc = order.PaymentConfirmedAtUtc
        };

        using (var seed = scenario.CreateContext())
        {
            seed.DeliveryDrivers.Add(driver);
            seed.Orders.Add(order);
            seed.StoreDeliveryDrivers.Add(link);
            seed.OrderDeliveryAssignments.Add(assignment);
            await seed.SaveChangesAsync();
        }

        scenario.OrderId = order.Id;
        scenario.DriverId = driver.Id;
        return scenario;
    }

    private sealed class PinScenario(string databaseName)
    {
        public Guid DriverUserId { get; } = Guid.NewGuid();
        public Guid CustomerUserId { get; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid DriverId { get; set; }

        public ApplicationDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options);
    }
}
