using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Moq;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class MockPixPaymentProcessorTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _db;
    private readonly Mock<INotificationService> _notificationMock;
    private readonly Mock<IOutboxWriter> _outboxWriterMock;
    private readonly FakeClock _clock;
    private readonly MockPixPaymentProcessor _sut;

    public MockPixPaymentProcessorTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-mock-pix-processor-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
        _notificationMock = new Mock<INotificationService>();
        _outboxWriterMock = new Mock<IOutboxWriter>();
        _clock = new FakeClock(FixedNow);
        _sut = new MockPixPaymentProcessor(
            _db,
            new EfUnitOfWork(_db),
            _notificationMock.Object,
            _outboxWriterMock.Object,
            _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldApproveDuePayment_AndAdvanceOrder()
    {
        var (order, payment) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(-1),
            expiresAtUtc: FixedNow.AddSeconds(30));

        var processed = await _sut.ProcessDuePaymentsAsync();

        processed.Should().Be(1);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Paid);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.Received);

        (await _db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == payment.Id
            && x.PreviousStatus == PaymentStatus.Pending
            && x.NewStatus == PaymentStatus.Paid
            && x.Source == "Mock")).Should().BeTrue();

        (await _db.OrderStatusHistories.AnyAsync(x => x.OrderId == order.Id
            && x.NewStatus == OrderStatus.Received)).Should().BeTrue();

        _notificationMock.Verify(x => x.NotifySellerNewOrderAsync(
            It.IsAny<Guid>(), order.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationMock.Verify(x => x.NotifyCustomerOrderStatusChangedAsync(
            order.CustomerUserId, order.Id, OrderStatus.Received, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        _outboxWriterMock.Verify(x => x.EnqueueAsync(
            OutboxEventTypes.OrderStatusChanged,
            order.Id,
            It.IsAny<OrderStatusChangedEvent>(),
            It.IsAny<DateTime>(),
            It.IsAny<long>(),
            nameof(Order),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldExpireDuePayment_AndKeepOrderPendingPayment()
    {
        var (order, payment) = await SeedAsync(
            MockPixOutcome.Expired,
            approvalAtUtc: null,
            expiresAtUtc: FixedNow.AddSeconds(-1));

        var processed = await _sut.ProcessDuePaymentsAsync();

        processed.Should().Be(1);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Failed);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.PendingPayment);

        (await _db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == payment.Id
            && x.PreviousStatus == PaymentStatus.Pending
            && x.NewStatus == PaymentStatus.Failed
            && x.Source == "Mock")).Should().BeTrue();

        (await _db.OrderStatusHistories.CountAsync(x => x.OrderId == order.Id)).Should().Be(0);

        _notificationMock.Verify(x => x.NotifySellerNewOrderAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxWriterMock.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
            It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldSkipPayments_NotYetDue()
    {
        var (_, _) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(20),
            expiresAtUtc: FixedNow.AddSeconds(60));

        var processed = await _sut.ProcessDuePaymentsAsync();

        processed.Should().Be(0);
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldIgnoreNonPixOnlineMockPayment()
    {
        var (_, payment) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(-1),
            expiresAtUtc: FixedNow.AddSeconds(30),
            method: PaymentMethod.CardOnline);

        var processed = await _sut.ProcessDuePaymentsAsync();

        processed.Should().Be(0);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldNotMarkPaid_WhenOrderIsCancelled()
    {
        var (order, payment) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(-1),
            expiresAtUtc: FixedNow.AddSeconds(30));

        var trackedOrder = await _db.Orders.SingleAsync(x => x.Id == order.Id);
        trackedOrder.Status = OrderStatus.Cancelled;
        await _db.SaveChangesAsync();

        var processed = await _sut.ProcessDuePaymentsAsync();

        processed.Should().Be(1);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Failed);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.Cancelled);

        (await _db.OrderStatusHistories.CountAsync(x => x.OrderId == order.Id)).Should().Be(0);
        _outboxWriterMock.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
            It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldNotReprocessTerminalPayment()
    {
        var (order, payment) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(-1),
            expiresAtUtc: FixedNow.AddSeconds(30));
        await _sut.ProcessDuePaymentsAsync();

        var processedAgain = await _sut.ProcessDuePaymentsAsync();

        processedAgain.Should().Be(0);

        var historyCount = await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id && x.NewStatus == PaymentStatus.Paid);
        historyCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldIgnoreConcurrentTransition_AndPreserveStateMachine()
    {
        var (order, payment) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(-1),
            expiresAtUtc: FixedNow.AddSeconds(30));

        var uow = new Mock<IEfUnitOfWork>();
        var calls = 0;
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new DbUpdateConcurrencyException("concurrent update", new List<IUpdateEntry>());
                }
                return _db.SaveChangesAsync(ct);
            });

        var sut = new MockPixPaymentProcessor(_db, uow.Object, _notificationMock.Object, _outboxWriterMock.Object, _clock);

        var processed = await sut.ProcessDuePaymentsAsync();

        processed.Should().Be(0);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Pending);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id)).Should().Be(0);
        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldNotPersistStagedNotificationsOrOutbox_WhenApprovalLosesConcurrencyRace()
    {
        var (order, payment) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(-1),
            expiresAtUtc: FixedNow.AddSeconds(30));

        var uow = new Mock<IEfUnitOfWork>();
        var calls = 0;
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new DbUpdateConcurrencyException("concurrent update", new List<IUpdateEntry>());
                }
                return _db.SaveChangesAsync(ct);
            });

        var sut = new MockPixPaymentProcessor(
            _db,
            uow.Object,
            new NotificationService(_db),
            new OutboxWriter(_db),
            _clock);

        var processed = await sut.ProcessDuePaymentsAsync();

        processed.Should().Be(0);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Pending);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.PendingPayment);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id)).Should().Be(0);
        (await _db.OrderStatusHistories.CountAsync(x => x.OrderId == order.Id)).Should().Be(0);
        (await _db.Notifications.CountAsync()).Should().Be(0);
        (await _db.OutboxMessages.CountAsync()).Should().Be(0);

        (await _db.AuditLogs.CountAsync(x => x.Event == "MockPixPaymentConcurrentConflict" && x.EntityId == payment.Id)).Should().Be(1);
    }

    [Fact]
    public async Task ProcessDuePaymentsAsync_ShouldProcessRemainingPayments_WhenOneLosesConcurrencyRace()
    {
        var (firstOrder, firstPayment) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(-1),
            expiresAtUtc: FixedNow.AddSeconds(30));
        var (secondOrder, secondPayment) = await SeedAsync(
            MockPixOutcome.Approved,
            approvalAtUtc: FixedNow.AddSeconds(-1),
            expiresAtUtc: FixedNow.AddSeconds(30));

        var uow = new Mock<IEfUnitOfWork>();
        var calls = 0;
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new DbUpdateConcurrencyException("concurrent update", new List<IUpdateEntry>());
                }
                return _db.SaveChangesAsync(ct);
            });

        var sut = new MockPixPaymentProcessor(_db, uow.Object, _notificationMock.Object, _outboxWriterMock.Object, _clock);

        var processed = await sut.ProcessDuePaymentsAsync();

        processed.Should().Be(1);

        var allPayments = await _db.Payments.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var processedPayment = allPayments.Single(x => x.Status == PaymentStatus.Paid);
        var unprocessedPayment = allPayments.Single(x => x.Status == PaymentStatus.Pending);

        (await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == processedPayment.OrderId)).Status
            .Should().Be(OrderStatus.Received);
        (await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == unprocessedPayment.OrderId)).Status
            .Should().Be(OrderStatus.PendingPayment);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == processedPayment.Id
            && x.NewStatus == PaymentStatus.Paid)).Should().Be(1);
        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == unprocessedPayment.Id)).Should().Be(0);
    }

    private async Task<(Order Order, Payment Payment)> SeedAsync(
        MockPixOutcome outcome,
        DateTime? approvalAtUtc,
        DateTime? expiresAtUtc,
        PaymentMethod method = PaymentMethod.PixOnline)
    {
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Mock Pix",
            Slug = $"loja-mock-{Guid.NewGuid():N}"
        };
        _db.Stores.Add(store);

        var order = new Order
        {
            Code = Guid.NewGuid().ToString("N")[..16],
            CustomerUserId = Guid.NewGuid(),
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.PixOnline,
            Status = OrderStatus.PendingPayment,
            Subtotal = 30m,
            DeliveryFee = 5m,
            Total = 35m
        };
        _db.Orders.Add(order);

        var payment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            GatewayTransactionId = $"mock_{order.Id:N}_1",
            GatewayCheckoutUrl = MockPixPaymentStrategy.MockCheckoutUrl,
            Method = method,
            Amount = order.Total,
            Status = PaymentStatus.Pending,
            Attempt = 1,
            MockOutcome = outcome.ToString(),
            MockApprovalAtUtc = approvalAtUtc,
            MockExpiresAtUtc = expiresAtUtc
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        return (order, payment);
    }

    private sealed class FakeClock : IMockPixClock
    {
        public FakeClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }
}
