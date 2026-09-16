using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class MercadoPagoOrderPaymentStrategyTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IMercadoPagoCheckoutAdapter> _adapterMock;
    private readonly MercadoPagoOrderPaymentStrategy _sut;

    public MercadoPagoOrderPaymentStrategyTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-mp-strategy-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
        _adapterMock = new Mock<IMercadoPagoCheckoutAdapter>();
        _sut = new MercadoPagoOrderPaymentStrategy(_db, _adapterMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task StartAsync_ShouldReuseExistingPayment_WithoutCallingGateway_WhenTransactionAlreadyExists()
    {
        var order = CreateOrder();
        var existingPayment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_existing",
            GatewayCheckoutUrl = null,
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Pending
        };

        var result = await _sut.StartAsync(order, existingPayment, CancellationToken.None);

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.Should().NotBeNull();
        result.GatewayTransactionId.Should().Be("pref_existing");
    }

    [Fact]
    public async Task StartAsync_ShouldReusePendingPaymentWithCheckoutUrl_WithoutCallingGateway()
    {
        var order = CreateOrder();
        var existingPayment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_existing",
            GatewayCheckoutUrl = "https://checkout/pagamento",
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Pending
        };

        var result = await _sut.StartAsync(order, existingPayment, CancellationToken.None);

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.GatewayCheckoutUrl.Should().Be("https://checkout/pagamento");
    }

    [Fact]
    public async Task StartAsync_ShouldCreateCheckout_WithAttemptOneIdempotencyKey_WhenNoPaymentExists()
    {
        var order = CreateOrder();
        await SeedOrderContextAsync(order);

        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_new",
                CheckoutUrl = "https://checkout/novo",
                RawPayload = "{}"
            });

        var result = await _sut.StartAsync(order, null, CancellationToken.None);

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(
                It.Is<MercadoPagoCheckoutCreateRequest>(r => r.IdempotencyKey == $"{order.Id:N}:1"),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.Should().NotBeNull();
        result.GatewayTransactionId.Should().Be("pref_new");
        result.GatewayCheckoutUrl.Should().Be("https://checkout/novo");
    }

    [Fact]
    public async Task StartAsync_ShouldIncludeDeliveryFeeInGatewayCheckout_WhenOrderHasDeliveryFee()
    {
        var order = CreateOrder();
        await SeedOrderContextAsync(order);

        MercadoPagoCheckoutCreateRequest? captured = null;
        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<MercadoPagoCheckoutCreateRequest, Guid?, CancellationToken>((request, _, _) => captured = request)
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_fee",
                CheckoutUrl = "https://checkout/fee",
                RawPayload = "{}"
            });

        await _sut.StartAsync(order, null, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Items.Should().Contain(i => i.Title == "Taxa de entrega" && i.UnitPrice == order.DeliveryFee);
    }

    [Fact]
    public async Task StartAsync_ShouldNotIncludeDeliveryFeeItem_WhenOrderHasNoDeliveryFee()
    {
        var order = new Order
        {
            Code = "124",
            CustomerUserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            FulfillmentType = FulfillmentType.PickUp,
            PaymentMethod = PaymentMethod.PixOnline,
            Status = OrderStatus.PendingPayment,
            Subtotal = 30m,
            DeliveryFee = 0m,
            Total = 30m
        };
        await SeedOrderContextAsync(order);

        MercadoPagoCheckoutCreateRequest? captured = null;
        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<MercadoPagoCheckoutCreateRequest, Guid?, CancellationToken>((request, _, _) => captured = request)
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_no_fee",
                CheckoutUrl = "https://checkout/no-fee",
                RawPayload = "{}"
            });

        await _sut.StartAsync(order, null, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Items.Should().NotContain(i => i.Title == "Taxa de entrega");
    }

    [Fact]
    public async Task StartAsync_ShouldReturnPaidPayment_WithoutCallingGateway_AndNotRegressToPending()
    {
        var order = CreateOrder();
        await SeedOrderContextAsync(order);
        var existingPayment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_paid",
            GatewayCheckoutUrl = "https://checkout/pago",
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Paid
        };
        _db.Payments.Add(existingPayment);
        await _db.SaveChangesAsync();

        var result = await _sut.StartAsync(order, existingPayment, CancellationToken.None);

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.Should().NotBeNull();
        result.PaymentId.Should().Be(existingPayment.Id);
        result.Status.Should().Be(PaymentStatus.Paid);
        result.GatewayTransactionId.Should().Be("pref_paid");
    }

    [Fact]
    public async Task StartAsync_ShouldReturnRefundedPayment_WithoutCallingGateway_AndNotRegressToPending()
    {
        var order = CreateOrder();
        await SeedOrderContextAsync(order);
        var existingPayment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_refunded",
            GatewayCheckoutUrl = "https://checkout/estornado",
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Refunded
        };
        _db.Payments.Add(existingPayment);
        await _db.SaveChangesAsync();

        var result = await _sut.StartAsync(order, existingPayment, CancellationToken.None);

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.Should().NotBeNull();
        result.PaymentId.Should().Be(existingPayment.Id);
        result.Status.Should().Be(PaymentStatus.Refunded);
        result.GatewayTransactionId.Should().Be("pref_refunded");
    }

    [Fact]
    public async Task StartAsync_ShouldCreateNewCheckout_WithIncrementedAttempt_WhenExistingPaymentIsCancelled()
    {
        var order = CreateOrder();
        await SeedOrderContextAsync(order);
        var existingPayment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_cancelled",
            GatewayCheckoutUrl = "https://checkout/cancelado",
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Cancelled,
            Attempt = 1
        };
        _db.Payments.Add(existingPayment);
        await _db.SaveChangesAsync();

        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_new",
                CheckoutUrl = "https://checkout/novo",
                RawPayload = "{}"
            });

        var result = await _sut.StartAsync(order, existingPayment, CancellationToken.None);

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(
                It.Is<MercadoPagoCheckoutCreateRequest>(r => r.IdempotencyKey == $"{order.Id:N}:2"),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.Should().NotBeNull();
        result.PaymentId.Should().Be(existingPayment.Id);
        result.GatewayTransactionId.Should().Be("pref_new");
        result.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task StartAsync_ShouldCreateNewCheckout_WithIncrementedAttempt_WhenExistingPaymentIsFailed()
    {
        var order = CreateOrder();
        await SeedOrderContextAsync(order);
        var existingPayment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_failed",
            GatewayCheckoutUrl = "https://checkout/falhou",
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Failed,
            Attempt = 2
        };
        _db.Payments.Add(existingPayment);
        await _db.SaveChangesAsync();

        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_retry",
                CheckoutUrl = "https://checkout/retry",
                RawPayload = "{}"
            });

        var result = await _sut.StartAsync(order, existingPayment, CancellationToken.None);

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(
                It.Is<MercadoPagoCheckoutCreateRequest>(r => r.IdempotencyKey == $"{order.Id:N}:3"),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.Should().NotBeNull();
        result.PaymentId.Should().Be(existingPayment.Id);
        result.GatewayTransactionId.Should().Be("pref_retry");
        result.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task StartAsync_ShouldClearMockSimulationMetadata_WhenReusingPayment()
    {
        var order = CreateOrder();
        await SeedOrderContextAsync(order);
        var existingPayment = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_failed",
            GatewayCheckoutUrl = "https://checkout/falhou",
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Failed,
            Attempt = 1,
            MockExpiresAtUtc = new DateTime(2026, 8, 30, 12, 1, 0, DateTimeKind.Utc),
            MockApprovalAtUtc = new DateTime(2026, 8, 30, 12, 0, 30, DateTimeKind.Utc),
            MockOutcome = MockPixOutcome.Approved.ToString()
        };
        _db.Payments.Add(existingPayment);
        await _db.SaveChangesAsync();

        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_retry",
                CheckoutUrl = "https://checkout/retry",
                RawPayload = "{}"
            });

        var result = await _sut.StartAsync(order, existingPayment, CancellationToken.None);
        await _db.SaveChangesAsync();

        result.Should().NotBeNull();
        result.PaymentId.Should().Be(existingPayment.Id);
        result.ExpiresAtUtc.Should().BeNull();

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.Id == existingPayment.Id);
        reloaded.MockExpiresAtUtc.Should().BeNull();
        reloaded.MockApprovalAtUtc.Should().BeNull();
        reloaded.MockOutcome.Should().BeNull();
    }

    private async Task SeedOrderContextAsync(Order order)
    {
        _db.Users.Add(new IdentityUser<Guid>
        {
            Id = order.CustomerUserId,
            UserName = "cliente@teste.com",
            Email = "cliente@teste.com"
        });
        _db.OrderItems.Add(new OrderItem
        {
            OrderId = order.Id,
            ProductName = "Pizza",
            Quantity = 1,
            UnitPrice = order.Total,
            TotalPrice = order.Total
        });
        await _db.SaveChangesAsync();
    }

    private static Order CreateOrder()
    {
        return new Order
        {
            Code = "123",
            CustomerUserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.PixOnline,
            Status = OrderStatus.PendingPayment,
            Subtotal = 30m,
            DeliveryFee = 5m,
            Total = 35m
        };
    }
}
