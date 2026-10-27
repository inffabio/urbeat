using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// Covers the optimistic-concurrency contract of the delivery PIN: two confirmations that read the
/// same assignment version may never both persist.
///
/// The races below use two independent <see cref="ApplicationDbContext"/> instances over the same
/// in-memory database and preload the order/assignment in both contexts before the first write. That
/// reproduces the exact production race (two requests holding the same ConcurrencyVersion snapshot)
/// without depending on wall-clock thread scheduling, which is not deterministic on the in-memory
/// provider. The essential assertion is the persisted state: the assignment version advances once
/// per winning write and the loser resolves as conflict (wrong PIN) or idempotent success (correct
/// PIN already confirmed).
/// </summary>
public sealed class DeliveryDriverServicePinConcurrencyTests
{
    private const string Pin = "1234";
    private const string WrongPin = "4321";

    [Fact]
    public async Task ConfirmPin_ShouldPersistAtMostOneAttemptPerVersion_WhenTwoWrongAttemptsRace()
    {
        var scenario = await SeedScenarioAsync();
        using var firstContext = scenario.CreateContext();
        using var secondContext = scenario.CreateContext();
        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        // Both requests snapshot the same assignment version before either writes, exactly like two
        // concurrent HTTP requests would.
        await firstContext.OrderDeliveryAssignments.SingleAsync();
        await secondContext.OrderDeliveryAssignments.SingleAsync();

        var winner = await firstService.ConfirmDeliveryPinAsync(
            scenario.DriverUserId, scenario.OrderId, WrongPin, ipAddress: null);
        var loser = await secondService.ConfirmDeliveryPinAsync(
            scenario.DriverUserId, scenario.OrderId, WrongPin, ipAddress: null);

        winner.ConcurrentUpdate.Should().BeFalse("the first attempt wins the version");
        winner.PinInvalid.Should().BeTrue();
        winner.RemainingAttempts.Should().Be(4);

        loser.ConcurrentUpdate.Should().BeTrue(
            "a stale attempt must be rejected instead of silently overwriting the counter");
        loser.PinLocked.Should().BeFalse();
        loser.RemainingAttempts.Should().Be(4, "the loser reports the counter that was actually persisted");

        using var verification = scenario.CreateContext();
        var persisted = await verification.OrderDeliveryAssignments
            .AsNoTracking()
            .SingleAsync(x => x.OrderId == scenario.OrderId);

        persisted.PinAttemptCount.Should().Be(
            1,
            "only one attempt may be persisted per assignment version; the losing request must not double count");
        persisted.ConcurrencyVersion.Should().Be(1, "the concurrency token advances exactly once");
    }

    [Fact]
    public async Task ConfirmPin_ShouldBeIdempotent_WhenTwoCorrectConfirmationsRace()
    {
        var scenario = await SeedScenarioAsync();
        using var firstContext = scenario.CreateContext();
        using var secondContext = scenario.CreateContext();
        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        await firstContext.Orders.SingleAsync();
        await firstContext.OrderDeliveryAssignments.SingleAsync();
        await secondContext.Orders.SingleAsync();
        await secondContext.OrderDeliveryAssignments.SingleAsync();

        var winner = await firstService.ConfirmDeliveryPinAsync(
            scenario.DriverUserId, scenario.OrderId, Pin, ipAddress: null);
        var loser = await secondService.ConfirmDeliveryPinAsync(
            scenario.DriverUserId, scenario.OrderId, Pin, ipAddress: null);

        winner.Status.Should().Be(OrderStatus.Delivered);
        winner.DeliveryConfirmedAtUtc.Should().NotBeNull();

        // The loser lost the concurrency check but the delivery is already confirmed, so the
        // contract is idempotent success reporting the persisted confirmation.
        loser.ConcurrentUpdate.Should().BeFalse("an already confirmed delivery is reported as success");
        loser.DeliveryConfirmedAtUtc.Should().Be(winner.DeliveryConfirmedAtUtc);

        using var verification = scenario.CreateContext();
        var persistedOrder = await verification.Orders
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.OrderId);
        persistedOrder.Status.Should().Be(OrderStatus.Delivered);
        persistedOrder.DeliveryConfirmedAtUtc.Should().Be(winner.DeliveryConfirmedAtUtc);
        persistedOrder.DeliveryPinConfirmedByDriverId.Should().Be(scenario.DriverId);

        var persistedAssignment = await verification.OrderDeliveryAssignments
            .AsNoTracking()
            .SingleAsync(x => x.OrderId == scenario.OrderId);
        persistedAssignment.ConcurrencyVersion.Should().Be(
            1,
            "the second confirmation was rejected by the concurrency token instead of confirming twice");
        persistedAssignment.PinAttemptCount.Should().Be(0, "a correct PIN does not consume attempts");
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

    private static async Task<PinScenario> SeedScenarioAsync()
    {
        var scenario = new PinScenario($"urbeat-pin-race-{Guid.NewGuid()}");

        var storeId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var driver = new DeliveryDriver
        {
            UserId = scenario.DriverUserId,
            FullName = "Entregador Concorrencia",
            Email = "driver.concurrency@urbeat.local",
            NormalizedEmail = "DRIVER.CONCURRENCY@URBEAT.LOCAL",
            PhoneNumber = "11999990001"
        };
        var order = new Order
        {
            Code = "URB-PIN001",
            CustomerUserId = customerUserId,
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.OnDelivery,
            StatusVersion = 2,
            Subtotal = 20m,
            DeliveryFee = 4m,
            Total = 24m
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
            ConcurrencyVersion = 0
        };

        using var seedContext = scenario.CreateContext();
        seedContext.DeliveryDrivers.Add(driver);
        seedContext.StoreDeliveryDrivers.Add(link);
        seedContext.Orders.Add(order);
        seedContext.OrderDeliveryAssignments.Add(assignment);
        await seedContext.SaveChangesAsync();

        scenario.DriverId = driver.Id;
        scenario.OrderId = order.Id;
        return scenario;
    }

    private sealed class PinScenario
    {
        private readonly string _databaseName;

        public PinScenario(string databaseName)
        {
            _databaseName = databaseName;
        }

        public Guid DriverUserId { get; } = Guid.NewGuid();

        public Guid OrderId { get; set; }

        public Guid DriverId { get; set; }

        public ApplicationDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .Options);
    }
}
