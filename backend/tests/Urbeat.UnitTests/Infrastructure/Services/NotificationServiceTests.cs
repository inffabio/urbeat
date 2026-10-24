using FluentAssertions;
using Urbeat.Application.Interfaces;
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
        var publisher = new RecordingLiveNotificationPublisher();
        var sut = new NotificationService(_db, publisher);

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

        var send = publisher.Sends.Should().ContainSingle().Subject;
        send.Channel.Should().Be(LiveChannel.Customer);
        send.UserId.Should().Be(customerUserId);
        send.Method.Should().Be("OrderStatusUpdated");

        ReadPayloadProperty(send.Payload, "orderId").Should().Be(orderId);
        ReadPayloadProperty(send.Payload, "orderCode").Should().Be(orderCode);
        ReadPayloadProperty(send.Payload, "status").Should().Be(status);
        ReadPayloadProperty(send.Payload, "changedAtUtc").Should().Be(changedAtUtc);
    }

    [Fact]
    public async Task NotifyCustomerOrderStatusUpdatedAsync_ShouldNotPersistDurableNotification()
    {
        var publisher = new RecordingLiveNotificationPublisher();
        var sut = new NotificationService(_db, publisher);

        await sut.NotifyCustomerOrderStatusUpdatedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "URB-123456",
            OrderStatus.Preparing,
            DateTime.UtcNow);

        (await _db.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PushSellerNotificationAsync_ShouldSendReceiveSellerNotificationToSellerUser()
    {
        var publisher = new RecordingLiveNotificationPublisher();
        var sut = new NotificationService(_db, publisher);

        var sellerUserId = Guid.NewGuid();
        var notification = new Notification
        {
            RecipientUserId = sellerUserId,
            OrderId = Guid.NewGuid(),
            Type = NotificationType.NewOrder,
            Title = "Novo pedido recebido",
            Message = "Novo pedido URB-123456."
        };

        var delivered = await sut.PushSellerNotificationAsync(sellerUserId, notification);

        delivered.Should().BeTrue();
        var send = publisher.Sends.Should().ContainSingle().Subject;
        send.Channel.Should().Be(LiveChannel.Seller);
        send.UserId.Should().Be(sellerUserId);
        send.Method.Should().Be("ReceiveSellerNotification");
        ReadPayloadProperty(send.Payload, "id").Should().Be(notification.Id);
        ReadPayloadProperty(send.Payload, "orderId").Should().Be(notification.OrderId);
        ReadPayloadProperty(send.Payload, "type").Should().Be(NotificationType.NewOrder);
        ReadPayloadProperty(send.Payload, "title").Should().Be(notification.Title);
        ReadPayloadProperty(send.Payload, "message").Should().Be(notification.Message);
        ReadPayloadProperty(send.Payload, "createdAtUtc").Should().Be(notification.CreatedAtUtc);
    }

    [Fact]
    public async Task PushCustomerNotificationAsync_ShouldSendReceiveCustomerNotificationToCustomerUser()
    {
        var publisher = new RecordingLiveNotificationPublisher();
        var sut = new NotificationService(_db, publisher);

        var customerUserId = Guid.NewGuid();
        var notification = new Notification
        {
            RecipientUserId = customerUserId,
            OrderId = Guid.NewGuid(),
            Type = NotificationType.OrderReady,
            Title = "Pedido pronto",
            Message = "Seu pedido esta pronto."
        };

        var delivered = await sut.PushCustomerNotificationAsync(customerUserId, notification);

        delivered.Should().BeTrue();
        var send = publisher.Sends.Should().ContainSingle().Subject;
        send.Channel.Should().Be(LiveChannel.Customer);
        send.UserId.Should().Be(customerUserId);
        send.Method.Should().Be("ReceiveCustomerNotification");
        ReadPayloadProperty(send.Payload, "id").Should().Be(notification.Id);
        ReadPayloadProperty(send.Payload, "type").Should().Be(NotificationType.OrderReady);
    }

    [Fact]
    public async Task NotifySellerOrderAcceptedForPrintingAsync_ShouldSendOrderAcceptedForPrintingToSellerUser()
    {
        var publisher = new RecordingLiveNotificationPublisher();
        var sut = new NotificationService(_db, publisher);

        var sellerUserId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var acceptedAtUtc = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        var delivered = await sut.NotifySellerOrderAcceptedForPrintingAsync(
            sellerUserId,
            orderId,
            storeId,
            "URB-ACCEPT",
            2,
            acceptedAtUtc);

        delivered.Should().BeTrue();
        var send = publisher.Sends.Should().ContainSingle().Subject;
        send.Channel.Should().Be(LiveChannel.Seller);
        send.UserId.Should().Be(sellerUserId);
        send.Method.Should().Be("OrderAcceptedForPrinting");
        ReadPayloadProperty(send.Payload, "orderId").Should().Be(orderId);
        ReadPayloadProperty(send.Payload, "storeId").Should().Be(storeId);
        ReadPayloadProperty(send.Payload, "orderCode").Should().Be("URB-ACCEPT");
        ReadPayloadProperty(send.Payload, "sequence").Should().Be(2);
        ReadPayloadProperty(send.Payload, "acceptedAtUtc").Should().Be(acceptedAtUtc);
    }

    [Fact]
    public async Task LivePush_ShouldReturnTrue_WhenNoLivePublisherIsConfigured()
    {
        var sut = new NotificationService(_db);

        var delivered = await sut.NotifyCustomerOrderStatusUpdatedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "URB-123456",
            OrderStatus.Preparing,
            DateTime.UtcNow);

        // No live channel means no delivery to fail; durable notifications remain the fallback.
        delivered.Should().BeTrue();
    }

    [Fact]
    public async Task LivePush_ShouldReturnFalse_WhenPublisherReportsFailure()
    {
        var publisher = new RecordingLiveNotificationPublisher { Result = false };
        var sut = new NotificationService(_db, publisher);

        var delivered = await sut.NotifyCustomerOrderStatusUpdatedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "URB-123456",
            OrderStatus.Preparing,
            DateTime.UtcNow);

        // A real transport failure must surface (false) so the outbox handler can retry.
        delivered.Should().BeFalse();
    }

    private static object? ReadPayloadProperty(object payload, string name)
    {
        return payload.GetType().GetProperty(name)?.GetValue(payload);
    }

    public enum LiveChannel
    {
        Seller,
        Customer
    }

    public sealed record RecordedPublish(LiveChannel Channel, Guid UserId, string Method, object Payload);

    private sealed class RecordingLiveNotificationPublisher : ILiveNotificationPublisher
    {
        public List<RecordedPublish> Sends { get; } = new();

        public bool Result { get; init; } = true;

        public Task<bool> PublishSellerAsync(
            Guid userId,
            string method,
            object payload,
            CancellationToken cancellationToken = default)
        {
            Sends.Add(new RecordedPublish(LiveChannel.Seller, userId, method, payload));
            return Task.FromResult(Result);
        }

        public Task<bool> PublishCustomerAsync(
            Guid userId,
            string method,
            object payload,
            CancellationToken cancellationToken = default)
        {
            Sends.Add(new RecordedPublish(LiveChannel.Customer, userId, method, payload));
            return Task.FromResult(Result);
        }
    }
}
