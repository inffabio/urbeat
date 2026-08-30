using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Outbox.Handlers;

/// <summary>
/// Live SignalR push for durable notifications (new order, status changes).
///
/// SignalR has no server-side idempotency key, so delivery is at-least-once. A crash after a push
/// but before the message is marked processed can deliver the same live event more than once.
/// Clients must tolerate duplicates; the durable notification rows and the list/polling endpoints
/// remain the authoritative source of truth. No delivery marker is recorded, since it would falsely
/// imply exactly-once.
/// </summary>
public sealed class NotificationHandler : IOutboxEventHandler
{
    private readonly INotificationService _notificationService;
    private readonly ApplicationDbContext _dbContext;

    public NotificationHandler(INotificationService notificationService, ApplicationDbContext dbContext)
    {
        _notificationService = notificationService;
        _dbContext = dbContext;
    }

    public IReadOnlyCollection<string> SupportedTypes => new[]
    {
        OutboxEventTypes.OrderCreated,
        OutboxEventTypes.OrderStatusChanged
    };

    public async Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Type == OutboxEventTypes.OrderCreated)
        {
            var created = OutboxPayloadSerializer.Deserialize<OrderCreatedEvent>(message.Payload);
            return await PushForStatusAsync(created.CustomerUserId, created.SellerUserId, created.OrderId, created.Status, cancellationToken);
        }

        var changed = OutboxPayloadSerializer.Deserialize<OrderStatusChangedEvent>(message.Payload);
        return await PushForStatusAsync(changed.CustomerUserId, changed.SellerUserId, changed.OrderId, changed.NewStatus, cancellationToken);
    }

    private async Task<OutboxProcessingResult> PushForStatusAsync(
        Guid customerUserId,
        Guid sellerUserId,
        Guid orderId,
        OrderStatus status,
        CancellationToken cancellationToken)
    {
        var allDelivered = true;

        if (status == OrderStatus.Received)
        {
            var sellerNotification = await FindNotificationAsync(sellerUserId, orderId, NotificationType.NewOrder, cancellationToken);
            if (sellerNotification is not null)
            {
                if (!await _notificationService.PushSellerNotificationAsync(sellerUserId, sellerNotification, cancellationToken))
                {
                    allDelivered = false;
                }
            }
        }

        var customerType = NotificationService.MapCustomerNotificationType(status);
        if (customerType is not null)
        {
            var customerNotification = await FindNotificationAsync(customerUserId, orderId, customerType.Value, cancellationToken);
            if (customerNotification is not null)
            {
                if (!await _notificationService.PushCustomerNotificationAsync(customerUserId, customerNotification, cancellationToken))
                {
                    allDelivered = false;
                }
            }
        }

        return allDelivered
            ? OutboxProcessingResult.Ok()
            : OutboxProcessingResult.Retry("SignalR notification push failed; will retry.");
    }

    private async Task<Notification?> FindNotificationAsync(
        Guid recipientUserId,
        Guid orderId,
        NotificationType type,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.RecipientUserId == recipientUserId && x.OrderId == orderId && x.Type == type, cancellationToken);
    }
}
