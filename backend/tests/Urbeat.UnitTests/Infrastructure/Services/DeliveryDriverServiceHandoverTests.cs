using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// Covers the manual handover/payment confirmation that must happen before the delivery PIN for
/// orders created under the current pay-on-delivery contract, including authoritative cash change
/// validation. Legacy orders keep the previous PIN-only flow and are covered elsewhere.
/// </summary>
public sealed class DeliveryDriverServiceHandoverTests
{
    private const string Pin = "1234";

    [Fact]
    public async Task ConfirmPin_ShouldRequireHandover_WithoutConsumingAnAttempt()
    {
        var scenario = await SeedScenarioAsync(PaymentMethod.PixOnDelivery);
        using var db = scenario.CreateContext();
        var service = CreateService(db);

        var result = await service.ConfirmDeliveryPinAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            Pin,
            ipAddress: null);

        result.HandoverRequired.Should().BeTrue();
        result.PinInvalid.Should().BeFalse();

        using var verification = scenario.CreateContext();
        var assignment = await verification.OrderDeliveryAssignments
            .AsNoTracking()
            .SingleAsync(x => x.OrderId == scenario.OrderId);
        assignment.PinAttemptCount.Should().Be(0, "a missing handover is not a wrong PIN");
        assignment.HandoverConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmHandover_ShouldPersistUtcAudit_AndAllowThePin()
    {
        var scenario = await SeedScenarioAsync(PaymentMethod.PixOnDelivery);
        using var db = scenario.CreateContext();
        var service = CreateService(db);

        var handover = await service.ConfirmHandoverAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            new ConfirmOrderHandoverRequestDto(),
            ipAddress: null);

        handover.InvalidState.Should().BeFalse();
        handover.PaymentConfirmedAtUtc.Should().NotBeNull();
        handover.PaymentConfirmedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);

        using (var verification = scenario.CreateContext())
        {
            var order = await verification.Orders.AsNoTracking().SingleAsync(x => x.Id == scenario.OrderId);
            order.PaymentConfirmedAtUtc.Should().NotBeNull();
            order.PaymentConfirmedByDriverId.Should().Be(scenario.DriverId);

            var assignment = await verification.OrderDeliveryAssignments
                .AsNoTracking()
                .SingleAsync(x => x.OrderId == scenario.OrderId);
            assignment.HandoverConfirmedAtUtc.Should().Be(order.PaymentConfirmedAtUtc);

            var audit = await verification.AuditLogs
                .AsNoTracking()
                .SingleAsync(x => x.Event == "OrderPaymentHandoverConfirmed");
            audit.Description.Should().Contain("confirmou o recebimento");
        }

        var pin = await service.ConfirmDeliveryPinAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            Pin,
            ipAddress: null);

        pin.Status.Should().Be(OrderStatus.Delivered);
        pin.DeliveryConfirmedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmHandover_ShouldValidateAndPersistCashReceivedAndChange()
    {
        var scenario = await SeedScenarioAsync(
            PaymentMethod.CashOnDelivery,
            total: 24m,
            cashWithoutChange: false,
            cashChangeForAmount: 50m);
        using var db = scenario.CreateContext();
        var service = CreateService(db);

        var belowTotal = await service.ConfirmHandoverAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            new ConfirmOrderHandoverRequestDto { CashReceivedAmount = 20m },
            ipAddress: null);

        belowTotal.PaymentAmountInvalid.Should().BeTrue();
        belowTotal.PaymentError.Should().Contain("menor que o total");

        var missing = await service.ConfirmHandoverAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            new ConfirmOrderHandoverRequestDto(),
            ipAddress: null);

        missing.PaymentAmountInvalid.Should().BeTrue();

        // The customer's change forecast is not a cap: receiving 60 for a 24 total is valid and
        // the server computes the real change (36), even though the checkout forecast was 50.
        var success = await service.ConfirmHandoverAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            new ConfirmOrderHandoverRequestDto { CashReceivedAmount = 60m },
            ipAddress: null);

        success.PaymentAmountInvalid.Should().BeFalse();
        success.CashReceivedAmount.Should().Be(60m);
        success.CashChangeAmount.Should().Be(36m);

        using var verification = scenario.CreateContext();
        var order = await verification.Orders.AsNoTracking().SingleAsync(x => x.Id == scenario.OrderId);
        order.CashReceivedAmount.Should().Be(60m);
        order.CashChangeAmount.Should().Be(36m);
    }

    [Fact]
    public async Task ConfirmHandover_ShouldBeIdempotent_WhenRepeated()
    {
        var scenario = await SeedScenarioAsync(
            PaymentMethod.CashOnDelivery,
            total: 24m,
            cashWithoutChange: false,
            cashChangeForAmount: 50m);
        using var db = scenario.CreateContext();
        var service = CreateService(db);

        var first = await service.ConfirmHandoverAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            new ConfirmOrderHandoverRequestDto { CashReceivedAmount = 30m },
            ipAddress: null);

        var second = await service.ConfirmHandoverAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            new ConfirmOrderHandoverRequestDto { CashReceivedAmount = 50m },
            ipAddress: null);

        second.PaymentConfirmedAtUtc.Should().Be(first.PaymentConfirmedAtUtc);
        second.CashReceivedAmount.Should().Be(30m, "a repeat must never rewrite the collected amount");
        second.CashChangeAmount.Should().Be(6m);

        using var verification = scenario.CreateContext();
        var auditCount = await verification.AuditLogs
            .AsNoTracking()
            .CountAsync(x => x.Event == "OrderPaymentHandoverConfirmed");
        auditCount.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmHandover_ShouldRejectCashAmountOnNonCashOrders()
    {
        var scenario = await SeedScenarioAsync(PaymentMethod.DebitOnDelivery);
        using var db = scenario.CreateContext();
        var service = CreateService(db);

        var result = await service.ConfirmHandoverAsync(
            scenario.DriverUserId,
            scenario.OrderId,
            new ConfirmOrderHandoverRequestDto { CashReceivedAmount = 24m },
            ipAddress: null);

        result.PaymentAmountInvalid.Should().BeTrue();
    }

    private static DeliveryDriverService CreateService(ApplicationDbContext dbContext)
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
            Mock.Of<INotificationService>(),
            new OutboxWriter(dbContext),
            Mock.Of<IOrderService>(),
            Options.Create(new EmailConfirmationOptions()),
            NullLogger<DeliveryDriverService>.Instance);
    }

    private static async Task<HandoverScenario> SeedScenarioAsync(
        PaymentMethod paymentMethod,
        decimal total = 24m,
        bool? cashWithoutChange = null,
        decimal? cashChangeForAmount = null)
    {
        var scenario = new HandoverScenario($"urbeat-handover-{Guid.NewGuid()}");

        var storeId = Guid.NewGuid();
        var driver = new DeliveryDriver
        {
            UserId = scenario.DriverUserId,
            FullName = "Entregador Handover",
            Email = "driver.handover@urbeat.local",
            NormalizedEmail = "DRIVER.HANDOVER@URBEAT.LOCAL",
            PhoneNumber = "11999990001"
        };
        var order = new Order
        {
            Code = $"URB-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            CustomerUserId = Guid.NewGuid(),
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = paymentMethod,
            Status = OrderStatus.OnDelivery,
            StatusVersion = 2,
            Subtotal = total,
            DeliveryFee = 0m,
            Total = total,
            CashWithoutChange = cashWithoutChange,
            CashChangeForAmount = cashChangeForAmount
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
            DeliveryStartedAtUtc = DateTime.UtcNow
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

    private sealed class HandoverScenario(string databaseName)
    {
        public Guid DriverUserId { get; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid DriverId { get; set; }

        public ApplicationDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options);
    }
}
