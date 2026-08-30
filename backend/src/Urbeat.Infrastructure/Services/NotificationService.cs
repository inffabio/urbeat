using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed partial class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly dynamic? _sellerHub;
    private readonly dynamic? _customerHub;

    public NotificationService(ApplicationDbContext dbContext, object? sellerHub = null, object? customerHub = null)
    {
        _dbContext = dbContext;
        _sellerHub = sellerHub;
        _customerHub = customerHub;
    }

    public async Task<CustomerNotificationsResponseDto> ListCustomerNotificationsAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await _dbContext.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == customerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .Select(x => new CustomerNotificationResponseDto
            {
                Id = x.Id,
                OrderId = x.OrderId,
                Type = x.Type,
                Title = x.Title,
                Message = x.Message,
                IsRead = x.IsRead,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new CustomerNotificationsResponseDto
        {
            UnreadCount = notifications.Count(x => !x.IsRead),
            Items = notifications
        };
    }

    public async Task<SellerNotificationsResponseDto> ListSellerNotificationsAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await _dbContext.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == sellerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .Select(x => new SellerNotificationResponseDto
            {
                Id = x.Id,
                OrderId = x.OrderId,
                Type = x.Type,
                Title = x.Title,
                Message = x.Message,
                IsRead = x.IsRead,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new SellerNotificationsResponseDto
        {
            UnreadCount = notifications.Count(x => !x.IsRead),
            Items = notifications
        };
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, Guid recipientUserId, CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.Notifications
            .SingleOrDefaultAsync(x => x.Id == notificationId && x.RecipientUserId == recipientUserId, cancellationToken);

        if (notification is null) return false;

        notification.IsRead = true;
        notification.ReadAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Persists a durable "new order" notification for the seller. The row is added to the current
    /// change tracker only — the caller's unit-of-work owns the commit. SignalR live delivery is
    /// performed later by the outbox handler.
    /// </summary>
    public async Task NotifySellerNewOrderAsync(
        Guid sellerUserId,
        Guid orderId,
        string message,
        CancellationToken cancellationToken = default)
    {
        var alreadyExists = await _dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(x => x.RecipientUserId == sellerUserId && x.OrderId == orderId && x.Type == NotificationType.NewOrder, cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        var notification = new Notification
        {
            RecipientUserId = sellerUserId,
            OrderId = orderId,
            Type = NotificationType.NewOrder,
            Title = "Novo pedido recebido",
            Message = message,
            IsRead = false
        };

        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
    }

    /// <summary>
    /// Persists a durable customer notification for an order status change. The row is added to the
    /// current change tracker only — the caller's unit-of-work owns the commit. SignalR live delivery
    /// is performed later by the outbox handler.
    /// </summary>
    public async Task NotifyCustomerOrderStatusChangedAsync(
        Guid customerUserId,
        Guid orderId,
        OrderStatus status,
        string? message,
        CancellationToken cancellationToken = default)
    {
        var type = MapCustomerNotificationType(status);
        if (type is null)
        {
            return;
        }

        var alreadyExists = await _dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(x => x.RecipientUserId == customerUserId && x.OrderId == orderId && x.Type == type.Value, cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        var notification = new Notification
        {
            RecipientUserId = customerUserId,
            OrderId = orderId,
            Type = type.Value,
            Title = MapCustomerNotificationTitle(status),
            Message = message ?? $"O pedido {orderId} mudou para {status}.",
            IsRead = false
        };

        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
    }

    public async Task NotifySellerSubscriptionStatusAsync(
        Guid sellerUserId,
        Guid subscriptionReferenceId,
        NotificationType notificationType,
        string message,
        CancellationToken cancellationToken = default)
    {
        var alreadyExists = await _dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(x => x.RecipientUserId == sellerUserId && x.OrderId == subscriptionReferenceId && x.Type == notificationType, cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        var title = notificationType switch
        {
            NotificationType.SubscriptionDueSoon => "Assinatura próxima do vencimento",
            NotificationType.SubscriptionOverdue => "Assinatura vencida",
            NotificationType.StoreBlockedBySubscription => "Loja bloqueada por inadimplência",
            _ => "Notificação da assinatura"
        };

        var notification = new Notification
        {
            RecipientUserId = sellerUserId,
            OrderId = subscriptionReferenceId,
            Type = notificationType,
            Title = title,
            Message = message,
            IsRead = false
        };

        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
    }

    public static NotificationType? MapCustomerNotificationType(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Received => NotificationType.OrderReceived,
            OrderStatus.Preparing => NotificationType.OrderPreparing,
            OrderStatus.Ready => NotificationType.OrderReady,
            OrderStatus.OnDelivery => NotificationType.OrderOnDelivery,
            OrderStatus.Delivered => NotificationType.OrderDelivered,
            OrderStatus.Cancelled => NotificationType.OrderCancelled,
            _ => null
        };
    }

    private static string MapCustomerNotificationTitle(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Received => "Pedido recebido",
            OrderStatus.Preparing => "Pedido em preparo",
            OrderStatus.Ready => "Pedido pronto",
            OrderStatus.OnDelivery => "Pedido saiu para entrega",
            OrderStatus.Delivered => "Pedido entregue",
            OrderStatus.Cancelled => "Pedido cancelado",
            _ => "Pedido atualizado"
        };
    }
}
