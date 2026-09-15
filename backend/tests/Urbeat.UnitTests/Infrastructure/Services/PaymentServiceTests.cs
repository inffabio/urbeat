using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Options;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class PaymentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IMercadoPagoCheckoutAdapter> _adapterMock;
    private readonly FakeClock _clock;
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-payment-service-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
        _adapterMock = new Mock<IMercadoPagoCheckoutAdapter>();
        _clock = new FakeClock(DateTime.UtcNow);
        var strategy = new MercadoPagoOrderPaymentStrategy(_db, _adapterMock.Object);
        var factory = new OrderPaymentStrategyFactory([strategy]);
        _sut = new PaymentService(_db, new EfUnitOfWork(_db), factory, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldCreateSinglePayment_WhenStartedConcurrentlyForSameOrder()
    {
        var order = await SeedOrderAsync();

        var gatewayCalls = 0;
        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_concurrent",
                CheckoutUrl = "https://checkout/concorrente",
                RawPayload = "{}"
            })
            .Callback(() => Interlocked.Increment(ref gatewayCalls));

        var request = new CreateOrderPaymentRequestDto { OrderId = order.Id };

        var first = _sut.CreateOrderPaymentAsync(order.CustomerUserId, request, null, CancellationToken.None);
        var second = _sut.CreateOrderPaymentAsync(order.CustomerUserId, request, null, CancellationToken.None);

        var results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(r => r.Payment != null);
        results[0].Payment!.PaymentId.Should().Be(results[1].Payment!.PaymentId);
        results[0].Payment.GatewayTransactionId.Should().Be("pref_concurrent");

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        (await _db.Payments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldCreateNewAttempt_WhenExistingPaymentFailed_AndOrderPendingPayment()
    {
        var order = await SeedOrderAsync();
        var existing = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_failed",
            Method = order.PaymentMethod,
            Amount = order.Total,
            Status = PaymentStatus.Failed,
            Attempt = 1
        };
        _db.Payments.Add(existing);
        await _db.SaveChangesAsync();

        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_retry",
                CheckoutUrl = "https://checkout/retry",
                RawPayload = "{}"
            });

        var request = new CreateOrderPaymentRequestDto { OrderId = order.Id };

        var result = await _sut.CreateOrderPaymentAsync(order.CustomerUserId, request, null, CancellationToken.None);

        result.InvalidOrderState.Should().BeFalse();
        result.Payment.Should().NotBeNull();
        result.Payment!.PaymentId.Should().Be(existing.Id);
        result.Payment.GatewayTransactionId.Should().Be("pref_retry");

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(
                It.Is<MercadoPagoCheckoutCreateRequest>(r => r.IdempotencyKey == $"{order.Id:N}:2"),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        reloaded.Attempt.Should().Be(2);
        reloaded.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldCreateNewAttempt_WhenExistingPaymentCancelled_AndOrderPendingPayment()
    {
        var order = await SeedOrderAsync();
        var existing = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_cancelled",
            Method = order.PaymentMethod,
            Amount = order.Total,
            Status = PaymentStatus.Cancelled,
            Attempt = 1
        };
        _db.Payments.Add(existing);
        await _db.SaveChangesAsync();

        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_retry_cancelled",
                CheckoutUrl = "https://checkout/retry-cancelled",
                RawPayload = "{}"
            });

        var result = await _sut.CreateOrderPaymentAsync(
            order.CustomerUserId,
            new CreateOrderPaymentRequestDto { OrderId = order.Id },
            null,
            CancellationToken.None);

        result.InvalidOrderState.Should().BeFalse();
        result.Payment!.GatewayTransactionId.Should().Be("pref_retry_cancelled");

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        reloaded.Attempt.Should().Be(2);
        reloaded.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldNotReleaseOrderLock_WhenWaitIsCancelled()
    {
        var order = await SeedOrderAsync();
        var checkoutStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCheckout = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                checkoutStarted.SetResult();
                await releaseCheckout.Task;
                return new MercadoPagoCheckoutCreateResponse
                {
                    TransactionId = "pref_lock",
                    CheckoutUrl = "https://checkout/lock",
                    RawPayload = "{}"
                };
            });

        var request = new CreateOrderPaymentRequestDto { OrderId = order.Id };
        var first = _sut.CreateOrderPaymentAsync(order.CustomerUserId, request, null);
        await checkoutStarted.Task;

        using var cancellation = new CancellationTokenSource();
        var second = _sut.CreateOrderPaymentAsync(order.CustomerUserId, request, null, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        releaseCheckout.SetResult();
        await first;
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldReject_WhenOrderCancelled()
    {
        var order = await SeedOrderAsync();
        order.Status = OrderStatus.Cancelled;
        await _db.SaveChangesAsync();

        var request = new CreateOrderPaymentRequestDto { OrderId = order.Id };

        var result = await _sut.CreateOrderPaymentAsync(order.CustomerUserId, request, null, CancellationToken.None);

        result.InvalidOrderState.Should().BeTrue();
        result.Payment.Should().BeNull();
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldReject_WhenOrderDelivered()
    {
        var order = await SeedOrderAsync();
        order.Status = OrderStatus.Delivered;
        await _db.SaveChangesAsync();

        var request = new CreateOrderPaymentRequestDto { OrderId = order.Id };

        var result = await _sut.CreateOrderPaymentAsync(order.CustomerUserId, request, null, CancellationToken.None);

        result.InvalidOrderState.Should().BeTrue();
        result.Payment.Should().BeNull();
    }

    [Fact]
    public async Task GetOrderPaymentAsync_ShouldNotExposeExpiresAtUtc_ForNonMockGateway()
    {
        var order = await SeedOrderAsync();
        _db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_mp",
            Method = order.PaymentMethod,
            Amount = order.Total,
            Status = PaymentStatus.Pending,
            MockExpiresAtUtc = new DateTime(2026, 8, 30, 12, 1, 0, DateTimeKind.Utc)
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetOrderPaymentAsync(order.CustomerUserId, order.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.ExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetOrderPaymentAsync_ShouldExposeExpiresAtUtc_ForMockGateway()
    {
        var order = await SeedOrderAsync();
        var expiresAtUtc = new DateTime(2026, 8, 30, 12, 1, 0, DateTimeKind.Utc);
        _db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            GatewayTransactionId = "mock_tx",
            Method = order.PaymentMethod,
            Amount = order.Total,
            Status = PaymentStatus.Pending,
            MockExpiresAtUtc = expiresAtUtc
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetOrderPaymentAsync(order.CustomerUserId, order.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.ExpiresAtUtc.Should().Be(expiresAtUtc);
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldRetryIdempotently_WhenWorkerFinalizesMockPaymentDuringRetry()
    {
        var order = await SeedOrderAsync();
        var now = _clock.UtcNow;

        var mockStrategy = CreateMockStrategy(_clock, out _);

        var expiredPending = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            GatewayTransactionId = "mock_expired",
            GatewayCheckoutUrl = MockPixPaymentStrategy.MockCheckoutUrl,
            Method = order.PaymentMethod,
            Amount = order.Total,
            Status = PaymentStatus.Pending,
            Attempt = 1,
            MockExpiresAtUtc = now.AddSeconds(-10),
            MockOutcome = MockPixOutcome.Expired.ToString()
        };
        _db.Payments.Add(expiredPending);
        await _db.SaveChangesAsync();

        // Simulate the mock Pix worker winning the optimistic-concurrency race on the first save: the
        // retry's write raises DbUpdateConcurrencyException, the committed state is re-read (still a
        // Pending mock attempt whose window elapsed) and a fresh attempt is started idempotently.
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

        var factory = new OrderPaymentStrategyFactory([mockStrategy]);
        var sut = new PaymentService(_db, uow.Object, factory, _clock);

        var result = await sut.CreateOrderPaymentAsync(
            order.CustomerUserId,
            new CreateOrderPaymentRequestDto { OrderId = order.Id },
            null,
            CancellationToken.None);

        result.InvalidOrderState.Should().BeFalse();
        result.Payment.Should().NotBeNull();
        result.Payment!.Status.Should().Be(PaymentStatus.Pending);

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        reloaded.Status.Should().Be(PaymentStatus.Pending);
        reloaded.Attempt.Should().Be(2);
        reloaded.MockExpiresAtUtc.Should().BeAfter(now);
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldReturnCurrentPayment_WithoutRetryingAdapter_WhenMercadoPagoConcurrentUpdate()
    {
        var order = await SeedOrderAsync();
        var existing = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            GatewayTransactionId = "pref_failed",
            Method = order.PaymentMethod,
            Amount = order.Total,
            Status = PaymentStatus.Failed,
            Attempt = 1
        };
        _db.Payments.Add(existing);
        await _db.SaveChangesAsync();

        var gatewayCalls = 0;
        _adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = "pref_retry",
                CheckoutUrl = "https://checkout/retry",
                RawPayload = "{}"
            })
            .Callback(() => Interlocked.Increment(ref gatewayCalls));

        // A worker wins the optimistic-concurrency race on the first save. For a Mercado Pago
        // payment the committed state must be returned as-is: the strategy/adapter is never
        // re-executed, so the gateway is not contacted a second time.
        var uow = new Mock<IEfUnitOfWork>();
        var saves = 0;
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) =>
            {
                saves++;
                if (saves == 1)
                {
                    throw new DbUpdateConcurrencyException("concurrent update", new List<IUpdateEntry>());
                }

                return _db.SaveChangesAsync(ct);
            });

        var strategy = new MercadoPagoOrderPaymentStrategy(_db, _adapterMock.Object);
        var sut = new PaymentService(_db, uow.Object, new OrderPaymentStrategyFactory([strategy]), _clock);

        var result = await sut.CreateOrderPaymentAsync(
            order.CustomerUserId,
            new CreateOrderPaymentRequestDto { OrderId = order.Id },
            null,
            CancellationToken.None);

        result.InvalidOrderState.Should().BeFalse();
        result.Payment.Should().NotBeNull();
        result.Payment!.PaymentId.Should().Be(existing.Id);
        result.Payment.Status.Should().Be(PaymentStatus.Failed);
        result.Payment.GatewayTransactionId.Should().Be("pref_failed");

        gatewayCalls.Should().Be(1);
        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(
                It.IsAny<MercadoPagoCheckoutCreateRequest>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrderPaymentAsync_ShouldUseMockStrategy_AndNeverCallMercadoPagoAdapter()
    {
        var order = await SeedOrderAsync();

        var mockStrategy = CreateMockStrategy(_clock, out _);
        var mercadoPagoStrategy = new MercadoPagoOrderPaymentStrategy(_db, _adapterMock.Object);
        var factory = new OrderPaymentStrategyFactory([mockStrategy, mercadoPagoStrategy]);
        var sut = new PaymentService(_db, new EfUnitOfWork(_db), factory, _clock);

        var result = await sut.CreateOrderPaymentAsync(
            order.CustomerUserId,
            new CreateOrderPaymentRequestDto { OrderId = order.Id },
            null,
            CancellationToken.None);

        result.Payment.Should().NotBeNull();
        result.Payment!.Gateway.Should().Be(PaymentGateway.Mock);
        result.Payment.GatewayCheckoutUrl.Should().Be(MockPixPaymentStrategy.MockCheckoutUrl);

        _adapterMock.Verify(
            x => x.CreateCheckoutAsync(
                It.IsAny<MercadoPagoCheckoutCreateRequest>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private MockPixPaymentStrategy CreateMockStrategy(IMockPixClock clock, out FakeRandom random)
    {
        random = new FakeRandom();
        return new MockPixPaymentStrategy(
            _db,
            Options.Create(new MockPixOptions { Provider = "Mock" }),
            clock,
            random);
    }

    private async Task<Order> SeedOrderAsync()
    {
        var order = new Order
        {
            Code = Guid.NewGuid().ToString("N")[..16],
            CustomerUserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.PixOnline,
            Status = OrderStatus.PendingPayment,
            Subtotal = 30m,
            DeliveryFee = 5m,
            Total = 35m
        };

        _db.Orders.Add(order);
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
        return order;
    }

    private sealed class FakeClock : IMockPixClock
    {
        public FakeClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }

    private sealed class FakeRandom : IMockPixRandom
    {
        private readonly Queue<int> _values = new();

        public void Enqueue(params int[] values)
        {
            foreach (var value in values)
            {
                _values.Enqueue(value);
            }
        }

        public int Next(int minimumInclusive, int maximumExclusive) =>
            _values.Count > 0 ? _values.Dequeue() : minimumInclusive;
    }
}
