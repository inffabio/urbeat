using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Services;

public sealed partial class NotificationService
{
    public async Task<bool> PushSellerNotificationAsync(
        Guid sellerUserId,
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        return await TrySendNotificationAsync(_sellerHub, sellerUserId.ToString(), "ReceiveSellerNotification", new
        {
            notification.Id,
            notification.OrderId,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.CreatedAtUtc
        }, cancellationToken);
    }

    public async Task<bool> PushCustomerNotificationAsync(
        Guid customerUserId,
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        return await TrySendNotificationAsync(_customerHub, customerUserId.ToString(), "ReceiveCustomerNotification", new
        {
            notification.Id,
            notification.OrderId,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.CreatedAtUtc
        }, cancellationToken);
    }

    public async Task<bool> NotifyCustomerOrderStatusUpdatedAsync(
        Guid customerUserId,
        Guid orderId,
        string orderCode,
        OrderStatus status,
        DateTime changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return await TrySendNotificationAsync(_customerHub, customerUserId.ToString(), "OrderStatusUpdated", new
        {
            orderId,
            orderCode,
            status,
            changedAtUtc
        }, cancellationToken);
    }

    private static async Task<bool> TrySendNotificationAsync(dynamic? hub, string userId, string method, object arg, CancellationToken ct)
    {
        if (hub is null)
        {
            // No live channel is configured in this environment; durable notifications remain the
            // source of truth, so this is not a delivery failure.
            return true;
        }

        try
        {
            await hub.Clients.User(userId).SendCoreAsync(method, new object[] { arg }, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A real SignalR send failure must surface to the outbox handler so it can retry.
            return false;
        }
    }
}
