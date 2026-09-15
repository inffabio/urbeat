using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class MockPixPaymentStrategyTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _db;
    private readonly RecordingRandom _random;
    private readonly FakeClock _clock;
    private readonly MockPixPaymentStrategy _sut;

    public MockPixPaymentStrategyTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-mock-pix-strategy-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
        _random = new RecordingRandom();
        _clock = new FakeClock(FixedNow);
        _sut = new MockPixPaymentStrategy(
            _db,
            Options.Create(new MockPixOptions { Provider = "Mock" }),
            _clock,
            _random);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void CanHandle_ShouldHandlePixOnline_WhenMockEnabled()
    {
        _sut.CanHandle(PaymentMethod.PixOnline).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_ShouldNotHandleCardOnline_WhenMockEnabled()
    {
        _sut.CanHandle(PaymentMethod.CardOnline).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_ShouldNotHandleAnyMethod_WhenProviderIsNotMock()
    {
        var sut = new MockPixPaymentStrategy(
            _db,
            Options.Create(new MockPixOptions { Provider = "MercadoPago" }),
            _clock,
            _random);

        sut.CanHandle(PaymentMethod.PixOnline).Should().BeFalse();
        sut.CanHandle(PaymentMethod.CardOnline).Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_ShouldCreatePendingMockPayment_WithApprovalMetadata()
    {
        var order = CreateOrder();
        _random.Enqueue(0, 20); // outcome Approved, delay 20s

        var result = await _sut.StartAsync(order, null, CancellationToken.None);
        await _db.SaveChangesAsync();

        var payment = await _db.Payments.SingleAsync(x => x.OrderId == order.Id);

        result.Gateway.Should().Be(PaymentGateway.Mock);
        result.Status.Should().Be(PaymentStatus.Pending);
        result.GatewayCheckoutUrl.Should().Be(MockPixPaymentStrategy.MockCheckoutUrl);
        result.ExpiresAtUtc.Should().Be(FixedNow.AddSeconds(60));

        payment.GatewayTransactionId.Should().Be($"mock_{order.Id:N}_1");
        payment.MockExpiresAtUtc.Should().Be(FixedNow.AddSeconds(60));
        payment.MockApprovalAtUtc.Should().Be(FixedNow.AddSeconds(20));
        payment.MockOutcome.Should().Be(MockPixOutcome.Approved.ToString());
        payment.Attempt.Should().Be(1);

        _random.Calls.Should().Contain((0, 2));
        _random.Calls.Should().Contain((10, 51));
    }

    [Fact]
    public async Task StartAsync_ShouldCreateExpiredOutcome_WithoutApprovalTimestamp()
    {
        var order = CreateOrder();
        _random.Enqueue(1); // outcome Expired

        var result = await _sut.StartAsync(order, null, CancellationToken.None);
        await _db.SaveChangesAsync();

        var payment = await _db.Payments.SingleAsync(x => x.OrderId == order.Id);

        result.ExpiresAtUtc.Should().Be(FixedNow.AddSeconds(60));
        payment.MockApprovalAtUtc.Should().BeNull();
        payment.MockOutcome.Should().Be(MockPixOutcome.Expired.ToString());

        _random.Calls.Should().HaveCount(1);
        _random.Calls[0].Should().Be((0, 2));
    }

    [Fact]
    public async Task StartAsync_ShouldReusePendingAttempt_WithoutRollingNewOutcome()
    {
        var order = CreateOrder();
        _random.Enqueue(0, 20);
        var first = await _sut.StartAsync(order, null, CancellationToken.None);
        await _db.SaveChangesAsync();

        var existing = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        var callsBeforeReuse = _random.Calls.Count;

        var reused = await _sut.StartAsync(order, existing, CancellationToken.None);

        reused.PaymentId.Should().Be(first.PaymentId);
        reused.GatewayTransactionId.Should().Be(first.GatewayTransactionId);
        reused.ExpiresAtUtc.Should().Be(first.ExpiresAtUtc);
        _random.Calls.Should().HaveCount(callsBeforeReuse);
    }

    [Fact]
    public async Task StartAsync_ShouldCreateFreshAttempt_WithoutSecondOrder_WhenPaymentFailed()
    {
        var order = CreateOrder();
        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        var failed = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            GatewayTransactionId = "mock_old",
            GatewayCheckoutUrl = MockPixPaymentStrategy.MockCheckoutUrl,
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Failed,
            Attempt = 1,
            MockExpiresAtUtc = FixedNow.AddSeconds(-10),
            MockOutcome = MockPixOutcome.Expired.ToString()
        };
        _db.Payments.Add(failed);
        await _db.SaveChangesAsync();

        _random.Enqueue(0, 30);
        var result = await _sut.StartAsync(order, failed, CancellationToken.None);
        await _db.SaveChangesAsync();

        result.PaymentId.Should().Be(failed.Id);
        result.Status.Should().Be(PaymentStatus.Pending);
        result.GatewayTransactionId.Should().Be($"mock_{order.Id:N}_2");

        (await _db.Payments.CountAsync(x => x.OrderId == order.Id)).Should().Be(1);
        (await _db.Orders.CountAsync()).Should().Be(1);

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        reloaded.Attempt.Should().Be(2);

        (await _db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == failed.Id
            && x.PreviousStatus == PaymentStatus.Failed
            && x.NewStatus == PaymentStatus.Pending
            && x.Source == "Checkout")).Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_ShouldCreateFreshAttempt_WhenPendingAttemptHasExpired()
    {
        var order = CreateOrder();
        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        var expiredPending = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            GatewayTransactionId = "mock_old",
            GatewayCheckoutUrl = MockPixPaymentStrategy.MockCheckoutUrl,
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Pending,
            Attempt = 1,
            MockExpiresAtUtc = FixedNow.AddSeconds(-10),
            MockOutcome = MockPixOutcome.Expired.ToString()
        };
        _db.Payments.Add(expiredPending);
        await _db.SaveChangesAsync();

        _random.Enqueue(0, 30);
        var result = await _sut.StartAsync(order, expiredPending, CancellationToken.None);
        await _db.SaveChangesAsync();

        result.PaymentId.Should().Be(expiredPending.Id);
        result.Status.Should().Be(PaymentStatus.Pending);
        result.GatewayTransactionId.Should().Be($"mock_{order.Id:N}_2");
        result.ExpiresAtUtc.Should().Be(FixedNow.AddSeconds(60));

        (await _db.Payments.CountAsync(x => x.OrderId == order.Id)).Should().Be(1);
        (await _db.Orders.CountAsync()).Should().Be(1);

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        reloaded.Attempt.Should().Be(2);
        reloaded.MockExpiresAtUtc.Should().Be(FixedNow.AddSeconds(60));

        (await _db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == expiredPending.Id
            && x.PreviousStatus == PaymentStatus.Pending
            && x.NewStatus == PaymentStatus.Failed
            && x.Source == "Mock")).Should().BeTrue();

        (await _db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == expiredPending.Id
            && x.PreviousStatus == PaymentStatus.Failed
            && x.NewStatus == PaymentStatus.Pending
            && x.Source == "Checkout")).Should().BeTrue();

        (await _db.AuditLogs.AnyAsync(x => x.EntityId == expiredPending.Id
            && x.Event == "MockPixPaymentExpired")).Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_ShouldReturnPaidPayment_WithoutResetting()
    {
        var order = CreateOrder();
        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        var paid = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            GatewayTransactionId = "mock_paid",
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Paid,
            Attempt = 1
        };
        _db.Payments.Add(paid);
        await _db.SaveChangesAsync();

        var result = await _sut.StartAsync(order, paid, CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Paid);
        result.GatewayTransactionId.Should().Be("mock_paid");
        _random.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_ShouldRegenerateConcurrencyStamp_WhenStartingFreshAttempt()
    {
        var order = CreateOrder();
        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        var staleStamp = Guid.NewGuid();
        var expiredPending = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            GatewayTransactionId = "mock_old",
            GatewayCheckoutUrl = MockPixPaymentStrategy.MockCheckoutUrl,
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Pending,
            Attempt = 1,
            ConcurrencyStamp = staleStamp,
            MockExpiresAtUtc = FixedNow.AddSeconds(-10),
            MockOutcome = MockPixOutcome.Expired.ToString()
        };
        _db.Payments.Add(expiredPending);
        await _db.SaveChangesAsync();

        _random.Enqueue(0, 30);
        await _sut.StartAsync(order, expiredPending, CancellationToken.None);
        await _db.SaveChangesAsync();

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        reloaded.ConcurrencyStamp.Should().NotBe(staleStamp);
        reloaded.ConcurrencyStamp.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task StartAsync_ShouldRegenerateConcurrencyStamp_WhenRetryingFailedPayment()
    {
        var order = CreateOrder();
        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        var staleStamp = Guid.NewGuid();
        var failed = new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            GatewayTransactionId = "mock_old",
            GatewayCheckoutUrl = MockPixPaymentStrategy.MockCheckoutUrl,
            Method = PaymentMethod.PixOnline,
            Amount = order.Total,
            Status = PaymentStatus.Failed,
            Attempt = 1,
            ConcurrencyStamp = staleStamp,
            MockExpiresAtUtc = FixedNow.AddSeconds(-10),
            MockOutcome = MockPixOutcome.Expired.ToString()
        };
        _db.Payments.Add(failed);
        await _db.SaveChangesAsync();

        _random.Enqueue(0, 30);
        await _sut.StartAsync(order, failed, CancellationToken.None);
        await _db.SaveChangesAsync();

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        reloaded.ConcurrencyStamp.Should().NotBe(staleStamp);
        reloaded.ConcurrencyStamp.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task StartAsync_ShouldNotRegenerateConcurrencyStamp_WhenReusingPendingAttempt()
    {
        var order = CreateOrder();
        _random.Enqueue(0, 20);
        await _sut.StartAsync(order, null, CancellationToken.None);
        await _db.SaveChangesAsync();

        var existing = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        var stampBefore = existing.ConcurrencyStamp;

        await _sut.StartAsync(order, existing, CancellationToken.None);
        await _db.SaveChangesAsync();

        var reloaded = await _db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        reloaded.ConcurrencyStamp.Should().Be(stampBefore);
    }

    private static Order CreateOrder()
    {
        return new Order
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
    }

    private sealed class FakeClock : IMockPixClock
    {
        public FakeClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; set; }
    }

    private sealed class RecordingRandom : IMockPixRandom
    {
        private readonly Queue<int> _results = new();

        public List<(int Min, int Max)> Calls { get; } = new();

        public void Enqueue(params int[] values)
        {
            foreach (var value in values)
            {
                _results.Enqueue(value);
            }
        }

        public int Next(int minimumInclusive, int maximumExclusive)
        {
            Calls.Add((minimumInclusive, maximumExclusive));
            return _results.Count > 0 ? _results.Dequeue() : minimumInclusive;
        }
    }
}
