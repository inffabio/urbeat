using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class OrderOutboxIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApplicationDbContext _db;
    private readonly string _dbName;

    public OrderOutboxIntegrationTests()
    {
        _dbName = $"urbeat-order-outbox-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ConfirmAsync_ShouldCommitOrderAndOutboxMessage_Together()
    {
        var (store, product, customerUserId) = await SeedStoreAndProductAsync();

        var sut = CreateCheckoutService(new EfUnitOfWork(_db));

        var result = await sut.ConfirmAsync(
            customerUserId,
            new CheckoutRequestDto
            {
                StoreId = store.Id,
                FulfillmentType = FulfillmentType.PickUp,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                Items = new[] { new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 } }
            },
            "127.0.0.1");

        result.Confirmation.Should().NotBeNull();

        using var verify = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);

        (await verify.Orders.CountAsync()).Should().Be(1);
        var message = await verify.OutboxMessages.SingleAsync();
        message.Type.Should().Be(OutboxEventTypes.OrderCreated);
        message.AggregateId.Should().Be(result.Confirmation!.OrderId);
        message.Status.Should().Be(OutboxMessageStatus.Pending);

        var payload = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload, JsonOptions);
        payload!.OrderId.Should().Be(result.Confirmation.OrderId);
        payload.Code.Should().Be(result.Confirmation.Code);
        payload.StoreId.Should().Be(store.Id);
        payload.CustomerUserId.Should().Be(customerUserId);
        payload.SellerUserId.Should().Be(store.OwnerUserId);
    }

    [Fact]
    public async Task ConfirmAsync_ShouldNotLeaveOrderOrOutbox_WhenSaveFails()
    {
        var (store, product, customerUserId) = await SeedStoreAndProductAsync();

        var uow = new Mock<IEfUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated commit failure."));

        var sut = CreateCheckoutService(uow.Object);

        Func<Task> act = () => sut.ConfirmAsync(
            customerUserId,
            new CheckoutRequestDto
            {
                StoreId = store.Id,
                FulfillmentType = FulfillmentType.PickUp,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                Items = new[] { new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 } }
            },
            "127.0.0.1");

        await act.Should().ThrowAsync<DbUpdateException>();

        using var verify = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);
        (await verify.Orders.CountAsync()).Should().Be(0);
        (await verify.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ConfirmAsync_ShouldNotDeliverSignalR_BeforeCommit()
    {
        var (store, product, customerUserId) = await SeedStoreAndProductAsync();

        var notificationService = new Mock<INotificationService>();
        var sut = CreateCheckoutService(new EfUnitOfWork(_db), notificationService.Object);

        await sut.ConfirmAsync(
            customerUserId,
            new CheckoutRequestDto
            {
                StoreId = store.Id,
                FulfillmentType = FulfillmentType.PickUp,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                Items = new[] { new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 } }
            },
            "127.0.0.1");

        // The mutation path only persists the durable notification and enqueues the outbox event;
        // it must never push to SignalR directly (that is the outbox handler's job).
        notificationService.Verify(
            x => x.PushSellerNotificationAsync(It.IsAny<Guid>(), It.IsAny<Notification>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notificationService.Verify(
            x => x.NotifyCustomerOrderStatusUpdatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<OrderStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private CheckoutService CreateCheckoutService(IEfUnitOfWork uow, INotificationService? notificationService = null)
    {
        var userManager = new UserManager<IdentityUser<Guid>>(
            Mock.Of<IUserStore<IdentityUser<Guid>>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

        return new CheckoutService(
            _db,
            uow,
            notificationService ?? Mock.Of<INotificationService>(),
            new OutboxWriter(_db),
            userManager,
            new PricingService());
    }

    private async Task<(Store Store, Product Product, Guid CustomerUserId)> SeedStoreAndProductAsync()
    {
        var customerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            IsOpen = true,
            IsSubscriptionBlocked = false
        };
        var product = new Product
        {
            StoreId = store.Id,
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Price = 40m,
            SaleMode = "single",
            IsAvailable = true
        };
        _db.Stores.Add(store);
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return (store, product, customerUserId);
    }
}
