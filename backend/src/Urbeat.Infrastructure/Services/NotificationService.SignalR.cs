using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed partial class NotificationService : INotificationService
{
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
        await _dbContext.SaveChangesAsync(cancellationToken);

        await TrySendNotificationAsync(_sellerHub, sellerUserId.ToString(), "ReceiveSellerNotification", new {
            notification.Id,
            notification.OrderId,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.CreatedAtUtc
        }, cancellationToken);
    }

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
        await _dbContext.SaveChangesAsync(cancellationToken);

        await TrySendNotificationAsync(_sellerHub, sellerUserId.ToString(), "ReceiveSellerNotification", new {
            notification.Id,
            notification.OrderId,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.CreatedAtUtc
        }, cancellationToken);
    }

    public async Task NotifyCustomerOrderStatusChangedAsync(
        Guid customerUserId,
        Guid orderId,
        OrderStatus status,
        string? message,
        CancellationToken cancellationToken = default)
    {
        NotificationType? type = null;
        var title = string.Empty;

        switch (status)
        {
            case OrderStatus.Received:
                type = NotificationType.OrderReceived;
                title = "Pedido recebido";
                break;
            case OrderStatus.Preparing:
                type = NotificationType.OrderPreparing;
                title = "Pedido em preparo";
                break;
            case OrderStatus.Ready:
                type = NotificationType.OrderReady;
                title = "Pedido pronto";
                break;
            case OrderStatus.OnDelivery:
                type = NotificationType.OrderOnDelivery;
                title = "Pedido saiu para entrega";
                break;
            case OrderStatus.Delivered:
                type = NotificationType.OrderDelivered;
                title = "Pedido entregue";
                break;
            case OrderStatus.Cancelled:
                type = NotificationType.OrderCancelled;
                title = "Pedido cancelado";
                break;
        }

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
            Title = title,
            Message = message ?? $"O pedido {orderId} mudou para {status}.",
            IsRead = false
        };

        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await TrySendNotificationAsync(_customerHub, customerUserId.ToString(), "ReceiveCustomerNotification", new {
            notification.Id,
            notification.OrderId,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.CreatedAtUtc
        }, cancellationToken);
    }

    private static async Task TrySendNotificationAsync(dynamic? hub, string userId, string method, object arg, CancellationToken ct)
    {
        if (hub is null)
            return;

        try
        {
            await hub.Clients.User(userId).SendAsync(method, arg, ct);
        }
        catch
        {
            // SignalR hub context not available in the current environment
        }
    }
}