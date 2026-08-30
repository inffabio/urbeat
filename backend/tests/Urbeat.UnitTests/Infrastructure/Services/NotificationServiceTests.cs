using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class NotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public NotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-notifications-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task NotifyCustomerOrderStatusUpdatedAsync_ShouldSendOrderStatusUpdatedToCustomerUser()
    {
        var hub = new FakeCustomerHubContext();
        var sut = new NotificationService(_db, customerHub: hub);

        var customerUserId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string orderCode = "URB-123456";
        var status = OrderStatus.Preparing;
        var changedAtUtc = new DateTime(2026, 8, 18, 15, 30, 0, DateTimeKind.Utc);

        await sut.NotifyCustomerOrderStatusUpdatedAsync(
            customerUserId,
            orderId,
            orderCode,
            status,
            changedAtUtc);

        var send = hub.Clients.Sends.Should().ContainSingle().Subject;
        send.UserId.Should().Be(customerUserId.ToString());
        send.Method.Should().Be("OrderStatusUpdated");

        var payload = send.Args.Should().ContainSingle().Subject;
        ReadPayloadProperty(payload, "orderId").Should().Be(orderId);
        ReadPayloadProperty(payload, "orderCode").Should().Be(orderCode);
        ReadPayloadProperty(payload, "status").Should().Be(status);
        ReadPayloadProperty(payload, "changedAtUtc").Should().Be(changedAtUtc);
    }

    [Fact]
    public async Task NotifyCustomerOrderStatusUpdatedAsync_ShouldNotPersistDurableNotification()
    {
        var hub = new FakeCustomerHubContext();
        var sut = new NotificationService(_db, customerHub: hub);

        await sut.NotifyCustomerOrderStatusUpdatedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "URB-123456",
            OrderStatus.Preparing,
            DateTime.UtcNow);

        (await _db.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task NotifyCustomerOrderStatusUpdatedAsync_ShouldReturnTrue_WhenCustomerHubIsNull()
    {
        var sut = new NotificationService(_db);

        var delivered = await sut.NotifyCustomerOrderStatusUpdatedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "URB-123456",
            OrderStatus.Preparing,
            DateTime.UtcNow);

        // No hub context means no live channel; durable notifications remain the fallback, so this
        // is not treated as a delivery failure.
        delivered.Should().BeTrue();
    }

    [Fact]
    public async Task NotifyCustomerOrderStatusUpdatedAsync_ShouldReturnFalse_WhenHubSendFails()
    {
        var sut = new NotificationService(_db, customerHub: new ThrowingCustomerHubContext());

        var delivered = await sut.NotifyCustomerOrderStatusUpdatedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "URB-123456",
            OrderStatus.Preparing,
            DateTime.UtcNow);

        // A real SignalR send failure must surface (false) so the outbox handler can retry.
        delivered.Should().BeFalse();
    }

    private static object? ReadPayloadProperty(object payload, string name)
    {
        return payload.GetType().GetProperty(name)?.GetValue(payload);
    }

    public sealed class FakeCustomerHubContext
    {
        public FakeHubClients Clients { get; } = new();

        public sealed class FakeHubClients
        {
            public List<RecordedSend> Sends { get; } = new();

            public FakeClientProxy User(string userId) => new(userId, Sends);
        }

        public sealed class FakeClientProxy
        {
            private readonly string _userId;
            private readonly List<RecordedSend> _sends;

            public FakeClientProxy(string userId, List<RecordedSend> sends)
            {
                _userId = userId;
                _sends = sends;
            }

            public Task SendCoreAsync(string method, object[] args, CancellationToken cancellationToken)
            {
                _sends.Add(new RecordedSend(_userId, method, args));
                return Task.CompletedTask;
            }
        }
    }

    public sealed class ThrowingCustomerHubContext
    {
        public ThrowingHubClients Clients { get; } = new();

        public sealed class ThrowingHubClients
        {
            public ThrowingClientProxy User(string userId) => new();
        }

        public sealed class ThrowingClientProxy
        {
            public Task SendCoreAsync(string method, object[] args, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("SignalR unavailable");
            }
        }
    }

    public sealed record RecordedSend(string UserId, string Method, object[] Args);
}
