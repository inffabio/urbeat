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
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// Seller-initiated order cancellation. The server is authoritative: only Received/Preparing can be
/// cancelled, the actor is always the store, the reason is mandatory and every effect (history,
/// audit, durable notification and outbox event) is produced in the same unit of work.
/// </summary>
public sealed class OrderCancellationTests : IDisposable
{
    private const string CancelReason = "Cliente pediu para cancelar";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dbName = $"urbeat-cancel-{Guid.NewGuid()}";
    private readonly ApplicationDbContext _db;
    private readonly Mock<INotificationService> _notification = new();
    private readonly OrderService _sut;

    public OrderCancellationTests()
    {
        _db = NewContext();
        _sut = new OrderService(_db, new EfUnitOfWork(_db), _notification.Object, new OutboxWriter(_db));
    }

    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);

    private async Task<(Store Store, Order Order, Guid SellerUserId, Guid CustomerUserId)> SeedAsync(
        OrderStatus status,
        FulfillmentType fulfillmentType = FulfillmentType.Delivery,
        DateTime? sellerCompletedAtUtc = null,
        Guid? sellerUserId = null,
        Guid? customerUserId = null)
    {
        var seller = sellerUserId ?? Guid.NewGuid();
        var customer = customerUserId ?? Guid.NewGuid();
        var store = new Store { OwnerUserId = seller, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "URB-123456",
            CustomerUserId = customer,
            StoreId = store.Id,
            FulfillmentType = fulfillmentType,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Status = status,
            StatusVersion = 2,
            Subtotal = 42.5m,
            Total = 42.5m,
            SellerCompletedAtUtc = sellerCompletedAtUtc
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return (store, order, seller, customer);
    }

    private static CancelOrderRequestDto Request(string? reason = CancelReason) =>
        new() { Reason = reason };

    [Theory]
    [InlineData(OrderStatus.Received)]
    [InlineData(OrderStatus.Preparing)]
    public async Task CancelStoreOrderAsync_ShouldCancel_ForReceivedAndPreparing(OrderStatus status)
    {
        var (_, order, sellerUserId, customerUserId) = await SeedAsync(status);
        var before = DateTime.UtcNow;

        var result = await _sut.CancelStoreOrderAsync(sellerUserId, order.Id, Request(), "127.0.0.1");

        result.NotFound.Should().BeFalse();
        result.Forbidden.Should().BeFalse();
        result.InvalidState.Should().BeFalse();
        result.ReasonRequired.Should().BeFalse();
        result.ConcurrentUpdate.Should().BeFalse();
        result.Order.Should().NotBeNull();
        result.Order!.Status.Should().Be(OrderStatus.Cancelled);

        var persisted = await _db.Orders.SingleAsync();
        persisted.Status.Should().Be(OrderStatus.Cancelled);
        persisted.CancellationReason.Should().Be(CancelReason);
        persisted.CancelledBy.Should().Be(OrderCancelledBy.Store);
        persisted.CancelledAtUtc.Should().NotBeNull();
        persisted.CancelledAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        persisted.CancelledAtUtc.Should().BeOnOrAfter(before);
        persisted.StatusVersion.Should().Be(3);

        var history = await _db.OrderStatusHistories.SingleAsync();
        history.PreviousStatus.Should().Be(status);
        history.NewStatus.Should().Be(OrderStatus.Cancelled);
        history.ChangedByUserId.Should().Be(sellerUserId);
        history.Notes.Should().Be(CancelReason);
        history.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var audit = await _db.AuditLogs.SingleAsync();
        audit.Event.Should().Be("OrderCancelled");
        audit.UserId.Should().Be(sellerUserId);
        audit.EntityId.Should().Be(order.Id);
        audit.Description.Should().Contain(CancelReason);
        audit.IpAddress.Should().Be("127.0.0.1");

        _notification.Verify(
            x => x.NotifyCustomerOrderStatusChangedAsync(
                customerUserId,
                order.Id,
                OrderStatus.Cancelled,
                "Seu pedido foi cancelado pela loja.",
                It.IsAny<CancellationToken>()),
            Times.Once);

        var message = await _db.OutboxMessages.SingleAsync(x => x.Type == OutboxEventTypes.OrderStatusChanged);
        message.AggregateId.Should().Be(order.Id);
        message.Sequence.Should().Be(persisted.StatusVersion);
        var payload = JsonSerializer.Deserialize<OrderStatusChangedEvent>(message.Payload, JsonOptions);
        payload!.OrderId.Should().Be(order.Id);
        payload.PreviousStatus.Should().Be(status);
        payload.NewStatus.Should().Be(OrderStatus.Cancelled);
        payload.Source.Should().Be("Seller");
        payload.SellerUserId.Should().Be(sellerUserId);
        payload.CustomerUserId.Should().Be(customerUserId);
        payload.Sequence.Should().Be(persisted.StatusVersion);
        payload.ChangedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        payload.ChangedAtUtc.Should().Be(persisted.CancelledAtUtc);
    }

    [Theory]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OnDelivery)]
    [InlineData(OrderStatus.Delivered)]
    public async Task CancelStoreOrderAsync_ShouldReject_StatusesAfterPreparation(OrderStatus status)
    {
        var (_, order, sellerUserId, _) = await SeedAsync(status);

        var result = await _sut.CancelStoreOrderAsync(sellerUserId, order.Id, Request(), null);

        result.InvalidState.Should().BeTrue();
        result.Order.Should().BeNull();
        (await _db.Orders.SingleAsync()).Status.Should().Be(status);
        _db.OrderStatusHistories.Should().BeEmpty();
        _db.AuditLogs.Should().BeEmpty();
        _db.OutboxMessages.Should().BeEmpty();
        _notification.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelStoreOrderAsync_ShouldReject_A_PickupAlreadyCollected()
    {
        var (_, order, sellerUserId, _) = await SeedAsync(
            OrderStatus.Ready,
            FulfillmentType.PickUp,
            sellerCompletedAtUtc: DateTime.UtcNow);

        var result = await _sut.CancelStoreOrderAsync(sellerUserId, order.Id, Request(), null);

        result.InvalidState.Should().BeTrue();
        (await _db.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Ready);
        _db.OutboxMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelStoreOrderAsync_ShouldForbid_AnotherStore()
    {
        var (_, order, _, _) = await SeedAsync(OrderStatus.Received);
        var otherSeller = Guid.NewGuid();

        var result = await _sut.CancelStoreOrderAsync(otherSeller, order.Id, Request(), null);

        result.Forbidden.Should().BeTrue();
        (await _db.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Received);
        _db.OrderStatusHistories.Should().BeEmpty();
        _db.AuditLogs.Should().BeEmpty();
        _db.OutboxMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelStoreOrderAsync_ShouldReturnNotFound_ForAnUnknownOrder()
    {
        var result = await _sut.CancelStoreOrderAsync(Guid.NewGuid(), Guid.NewGuid(), Request(), null);

        result.NotFound.Should().BeTrue();
        _db.OutboxMessages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("abcd")]
    public async Task CancelStoreOrderAsync_ShouldRequireAReasonWithAtLeastFiveCharacters(string? reason)
    {
        var (_, order, sellerUserId, _) = await SeedAsync(OrderStatus.Received);

        var result = await _sut.CancelStoreOrderAsync(sellerUserId, order.Id, Request(reason), null);

        result.ReasonRequired.Should().BeTrue();
        (await _db.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Received);
        _db.OrderStatusHistories.Should().BeEmpty();
        _db.OutboxMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelStoreOrderAsync_ShouldReject_AReasonLongerThanTheColumn()
    {
        var (_, order, sellerUserId, _) = await SeedAsync(OrderStatus.Received);

        var result = await _sut.CancelStoreOrderAsync(
            sellerUserId,
            order.Id,
            Request(new string('a', CancelOrderRequestDto.MaximumReasonLength + 1)),
            null);

        result.ReasonTooLong.Should().BeTrue();
        result.ReasonRequired.Should().BeFalse();
        (await _db.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Received);
        _db.OutboxMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelStoreOrderAsync_ShouldTrimTheReason()
    {
        var (_, order, sellerUserId, _) = await SeedAsync(OrderStatus.Received);

        await _sut.CancelStoreOrderAsync(sellerUserId, order.Id, Request("   motivo válido   "), null);

        (await _db.Orders.SingleAsync()).CancellationReason.Should().Be("motivo válido");
        (await _db.OrderStatusHistories.SingleAsync()).Notes.Should().Be("motivo válido");
    }

    [Fact]
    public async Task CancelStoreOrderAsync_ShouldBeIdempotent_ForAnAlreadyCancelledOrder()
    {
        var (_, order, sellerUserId, _) = await SeedAsync(OrderStatus.Received);
        await _sut.CancelStoreOrderAsync(sellerUserId, order.Id, Request(), null);
        _notification.Invocations.Clear();

        var replay = await _sut.CancelStoreOrderAsync(sellerUserId, order.Id, Request("outro motivo"), null);

        replay.Order.Should().NotBeNull();
        replay.Order!.Status.Should().Be(OrderStatus.Cancelled);
        (await _db.OrderStatusHistories.CountAsync()).Should().Be(1);
        (await _db.AuditLogs.CountAsync()).Should().Be(1);
        (await _db.OutboxMessages.CountAsync()).Should().Be(1);
        _notification.VerifyNoOtherCalls();
        (await _db.Orders.SingleAsync()).CancellationReason.Should().Be(CancelReason);
    }

    /// <summary>
    /// The generic status PATCH must never cancel an order: doing so would bypass the reason,
    /// actor, UTC timestamp, history, audit and notification produced by the dedicated flow.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Created)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.Received)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OnDelivery)]
    public async Task UpdateStatusAsync_ShouldRejectCancelled_AndKeepTheDedicatedCancelFlow(OrderStatus status)
    {
        var (_, order, sellerUserId, _) = await SeedAsync(status);

        var result = await _sut.UpdateStatusAsync(
            sellerUserId,
            order.Id,
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Cancelled, Notes = "bypass" },
            "127.0.0.1");

        result.InvalidTransition.Should().BeTrue();
        result.Order.Should().BeNull();

        var persisted = await _db.Orders.SingleAsync();
        persisted.Status.Should().Be(status);
        persisted.CancellationReason.Should().BeNull();
        persisted.CancelledBy.Should().BeNull();
        persisted.CancelledAtUtc.Should().BeNull();
        persisted.StatusVersion.Should().Be(2);

        _db.OrderStatusHistories.Should().BeEmpty();
        _db.AuditLogs.Should().BeEmpty();
        _db.Notifications.Should().BeEmpty();
        _db.OutboxMessages.Should().BeEmpty();
        _notification.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldStillAllowTheNormalSellerTransitions()
    {
        var (_, order, sellerUserId, _) = await SeedAsync(OrderStatus.Received);

        var result = await _sut.UpdateStatusAsync(
            sellerUserId,
            order.Id,
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Preparing },
            null);

        result.InvalidTransition.Should().BeFalse();
        result.Order.Should().NotBeNull();
        result.Order!.Status.Should().Be(OrderStatus.Preparing);
        (await _db.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Preparing);
        (await _db.Orders.SingleAsync()).CancellationReason.Should().BeNull();
        (await _db.OrderStatusHistories.SingleAsync()).NewStatus.Should().Be(OrderStatus.Preparing);
        _db.OutboxMessages.Should().ContainSingle();
        _notification.Verify(
            x => x.NotifyCustomerOrderStatusChangedAsync(
                It.IsAny<Guid>(),
                order.Id,
                OrderStatus.Preparing,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelStoreOrderAsync_ShouldReportConcurrentUpdate_AndDetachTheLosingWrite()
    {
        var (_, order, sellerUserId, _) = await SeedAsync(OrderStatus.Received);
        var unitOfWork = new Mock<IEfUnitOfWork>();
        unitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());
        var service = new OrderService(_db, unitOfWork.Object, _notification.Object, new OutboxWriter(_db));

        var result = await service.CancelStoreOrderAsync(sellerUserId, order.Id, Request(), null);

        result.ConcurrentUpdate.Should().BeTrue();
        result.Order.Should().BeNull();
        _db.ChangeTracker.Entries().Should().BeEmpty();
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
