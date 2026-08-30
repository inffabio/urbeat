using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Moq;
using Npgsql;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class PaymentWebhookServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IMercadoPagoCheckoutAdapter> _adapterMock;
    private readonly Mock<INotificationService> _notificationMock;
    private readonly PaymentWebhookService _sut;

    public PaymentWebhookServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-payment-webhook-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
        _adapterMock = new Mock<IMercadoPagoCheckoutAdapter>();
        _notificationMock = new Mock<INotificationService>();
        _sut = new PaymentWebhookService(_db, new EfUnitOfWork(_db), _adapterMock.Object, _notificationMock.Object, Mock.Of<IOutboxWriter>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task LateRejectedWebhook_ShouldNotRegressPaidPaymentOrOrder()
    {
        var order = await SeedOrderAsync(OrderStatus.Received);
        var payment = await SeedPaymentAsync(order, "txn-paid", PaymentStatus.Paid);
        SetupAdapter("txn-paid", "rejected");

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-paid"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Paid);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id && x.NewStatus == PaymentStatus.Failed)).Should().Be(0);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.Received);
        (await _db.OrderStatusHistories.CountAsync(x => x.OrderId == order.Id && x.NewStatus == OrderStatus.Cancelled)).Should().Be(0);

        result.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task LateCancelledWebhook_ShouldNotRegressRefundedPayment()
    {
        var order = await SeedOrderAsync(OrderStatus.Cancelled);
        var payment = await SeedPaymentAsync(order, "txn-refunded", PaymentStatus.Refunded);
        SetupAdapter("txn-refunded", "cancelled");

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-refunded"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Refunded);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id && x.NewStatus == PaymentStatus.Cancelled)).Should().Be(0);

        result.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task LateRejectedWebhook_ShouldStillRecordEventKey_ForIdempotency()
    {
        var order = await SeedOrderAsync(OrderStatus.Received);
        await SeedPaymentAsync(order, "txn-late", PaymentStatus.Paid);
        SetupAdapter("txn-late", "rejected");

        await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-late"), null);

        (await _db.PaymentWebhookEvents.AnyAsync(x => x.Gateway == PaymentGateway.MercadoPago && x.EventKey == "txn-late:Failed")).Should().BeTrue();
    }

    [Fact]
    public async Task RefundWebhook_ShouldTransitionPaidToRefunded()
    {
        var order = await SeedOrderAsync(OrderStatus.Received);
        var payment = await SeedPaymentAsync(order, "txn-pay-refund", PaymentStatus.Paid);
        SetupAdapter("txn-pay-refund", "refunded");

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-pay-refund"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Refunded);

        (await _db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == payment.Id && x.PreviousStatus == PaymentStatus.Paid && x.NewStatus == PaymentStatus.Refunded)).Should().BeTrue();

        result.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovedWebhook_ShouldTransitionPendingToPaid_AndOrderToReceived()
    {
        var order = await SeedOrderAsync(OrderStatus.PendingPayment);
        var payment = await SeedPaymentAsync(order, "txn-approve", PaymentStatus.Pending);
        SetupAdapter("txn-approve", "approved");

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-approve"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Paid);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.Received);

        result.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateWebhook_ShouldBeIgnored()
    {
        var order = await SeedOrderAsync(OrderStatus.PendingPayment);
        await SeedPaymentAsync(order, "txn-dup", PaymentStatus.Pending);
        SetupAdapter("txn-dup", "approved");

        var first = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-dup"), null);
        var second = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-dup"), null);

        first.Processed.Should().BeTrue();
        second.Ignored.Should().BeTrue();

        (await _db.PaymentWebhookEvents.CountAsync(x => x.Gateway == PaymentGateway.MercadoPago && x.EventKey == "txn-dup:Paid")).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentDuplicateWebhook_ShouldBeIgnored_WhenUniqueConstraintRace()
    {
        var order = await SeedOrderAsync(OrderStatus.PendingPayment);
        await SeedPaymentAsync(order, "txn-race", PaymentStatus.Pending);
        SetupAdapter("txn-race", "approved");

        // Simulates the any/insert race: the unique constraint on (Gateway, EventKey) rejects the
        // losing request's save. The service must treat that as an idempotent duplicate, not fail.
        var uow = new Mock<IEfUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "save failed",
                new PostgresException(
                    "duplicate key value violates unique constraint \"IX_PaymentWebhookEvents_Gateway_EventKey\"",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.UniqueViolation)));
        var sut = new PaymentWebhookService(_db, uow.Object, _adapterMock.Object, _notificationMock.Object, Mock.Of<IOutboxWriter>());

        var result = await sut.ProcessMercadoPagoWebhookAsync(Payload("txn-race"), null);

        result.Ignored.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentDifferentStateWebhook_ShouldBeIgnored_AndPreserveStateMachine()
    {
        var order = await SeedOrderAsync(OrderStatus.PendingPayment);
        var payment = await SeedPaymentAsync(order, "txn-race-diff", PaymentStatus.Pending);
        SetupAdapter("txn-race-diff", "approved");

        // A concurrent webhook already transitioned the same payment; the losing request's save
        // raises an optimistic-concurrency exception. The service must not corrupt the state machine.
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

        var sut = new PaymentWebhookService(_db, uow.Object, _adapterMock.Object, _notificationMock.Object, Mock.Of<IOutboxWriter>());

        var result = await sut.ProcessMercadoPagoWebhookAsync(Payload("txn-race-diff"), null);

        result.Ignored.Should().BeTrue();

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Pending);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id)).Should().Be(0);
        (await _db.PaymentWebhookEvents.AnyAsync(x => x.EventKey == "txn-race-diff:Paid")).Should().BeTrue();
    }

    [Fact]
    public async Task LateApprovedWebhook_ShouldNotMarkPaid_WhenOrderIsCancelled()
    {
        var order = await SeedOrderAsync(OrderStatus.Cancelled);
        var payment = await SeedPaymentAsync(order, "txn-cancelled", PaymentStatus.Pending);
        SetupAdapter("txn-cancelled", "approved");

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-cancelled"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Pending);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id && x.NewStatus == PaymentStatus.Paid)).Should().Be(0);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.Cancelled);

        result.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task LateApprovedWebhook_ShouldNotMarkPaid_WhenOrderIsDelivered()
    {
        var order = await SeedOrderAsync(OrderStatus.Delivered);
        var payment = await SeedPaymentAsync(order, "txn-delivered", PaymentStatus.Pending);
        SetupAdapter("txn-delivered", "approved");

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-delivered"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Pending);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id && x.NewStatus == PaymentStatus.Paid)).Should().Be(0);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.Delivered);

        result.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovedWebhook_ShouldNotMarkPaid_WhenGatewayTransactionBelongsToOldAttempt()
    {
        var order = await SeedOrderAsync(OrderStatus.PendingPayment);
        var payment = await SeedPaymentAsync(order, "pref_current", PaymentStatus.Pending);

        _adapterMock
            .Setup(x => x.GetPaymentDetailsAsync("pref_current", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoPaymentDetails
            {
                TransactionId = "pref_old",
                Status = "approved",
                RawPayload = "{}"
            });

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("pref_current"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Pending);

        (await _db.PaymentStatusHistories.CountAsync(x => x.PaymentId == payment.Id && x.NewStatus == PaymentStatus.Paid)).Should().Be(0);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.PendingPayment);

        result.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task RejectedWebhook_ShouldMarkPaymentFailed_ButKeepOrderPendingPayment_ForRetry()
    {
        var order = await SeedOrderAsync(OrderStatus.PendingPayment);
        var payment = await SeedPaymentAsync(order, "txn-rejected", PaymentStatus.Pending);
        SetupAdapter("txn-rejected", "rejected");

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-rejected"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Failed);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.PendingPayment);

        (await _db.OrderStatusHistories.CountAsync(x => x.OrderId == order.Id && x.NewStatus == OrderStatus.Cancelled)).Should().Be(0);

        result.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task CancelledWebhook_ShouldMarkPaymentCancelled_ButKeepOrderPendingPayment_ForRetry()
    {
        var order = await SeedOrderAsync(OrderStatus.PendingPayment);
        var payment = await SeedPaymentAsync(order, "txn-mp-cancelled", PaymentStatus.Pending);
        SetupAdapter("txn-mp-cancelled", "cancelled");

        var result = await _sut.ProcessMercadoPagoWebhookAsync(Payload("txn-mp-cancelled"), null);

        var reloadedPayment = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        reloadedPayment.Status.Should().Be(PaymentStatus.Cancelled);

        var reloadedOrder = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        reloadedOrder.Status.Should().Be(OrderStatus.PendingPayment);

        (await _db.OrderStatusHistories.CountAsync(x => x.OrderId == order.Id && x.NewStatus == OrderStatus.Cancelled)).Should().Be(0);

        result.Processed.Should().BeTrue();
    }

    private void SetupAdapter(string transactionId, string status)
    {
        _adapterMock
            .Setup(x => x.GetPaymentDetailsAsync(transactionId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoPaymentDetails
            {
                TransactionId = transactionId,
                Status = status,
                RawPayload = "{}"
            });
    }

    private static string Payload(string transactionId)
        => $"{{\"type\":\"payment\",\"data\":{{\"id\":\"{transactionId}\"}}}}";

    private async Task<Order> SeedOrderAsync(OrderStatus status)
    {
        var order = new Order
        {
            Code = Guid.NewGuid().ToString("N")[..16],
            CustomerUserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.CardOnline,
            Status = status,
            Subtotal = 30m,
            DeliveryFee = 5m,
            Total = 35m
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    private async Task<Payment> SeedPaymentAsync(Order order, string transactionId, PaymentStatus status)
    {
        var payment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = transactionId,
            Method = PaymentMethod.CardOnline,
            Amount = order.Total,
            Status = status
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }
}
