using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Services;

public sealed partial class NotificationService
{
    public Task<bool> PushSellerNotificationAsync(
        Guid sellerUserId,
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        return SendToSellerAsync(sellerUserId, "ReceiveSellerNotification", new
        {
            id = notification.Id,
            orderId = notification.OrderId,
            type = notification.Type,
            title = notification.Title,
            message = notification.Message,
            createdAtUtc = notification.CreatedAtUtc
        }, cancellationToken);
    }

    public Task<bool> PushCustomerNotificationAsync(
        Guid customerUserId,
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        return SendToCustomerAsync(customerUserId, "ReceiveCustomerNotification", new
        {
            id = notification.Id,
            orderId = notification.OrderId,
            type = notification.Type,
            title = notification.Title,
            message = notification.Message,
            createdAtUtc = notification.CreatedAtUtc
        }, cancellationToken);
    }

    public Task<bool> NotifyCustomerOrderStatusUpdatedAsync(
        Guid customerUserId,
        Guid orderId,
        string orderCode,
        OrderStatus status,
        DateTime changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SendToCustomerAsync(customerUserId, "OrderStatusUpdated", new
        {
            orderId,
            orderCode,
            status,
            changedAtUtc
        }, cancellationToken);
    }

    public Task<bool> NotifySellerOrderStatusUpdatedAsync(
        Guid sellerUserId,
        Guid orderId,
        string orderCode,
        OrderStatus status,
        DateTime changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SendToSellerAsync(sellerUserId, "OrderStatusUpdated", new
        {
            orderId,
            orderCode,
            status,
            changedAtUtc
        }, cancellationToken);
    }

    public Task<bool> NotifySellerOrderAcceptedForPrintingAsync(
        Guid sellerUserId,
        Guid orderId,
        Guid storeId,
        string orderCode,
        int sequence,
        DateTime acceptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SendToSellerAsync(sellerUserId, "OrderAcceptedForPrinting", new
        {
            orderId,
            storeId,
            orderCode,
            sequence,
            acceptedAtUtc
        }, cancellationToken);
    }

    private Task<bool> SendToSellerAsync(
        Guid recipientUserId,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        // No live channel is configured in this environment; durable notifications remain the
        // source of truth, so this is not a delivery failure.
        return _livePublisher is null
            ? Task.FromResult(true)
            : _livePublisher.PublishSellerAsync(recipientUserId, method, payload, cancellationToken);
    }

    private Task<bool> SendToCustomerAsync(
        Guid recipientUserId,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        return _livePublisher is null
            ? Task.FromResult(true)
            : _livePublisher.PublishCustomerAsync(recipientUserId, method, payload, cancellationToken);
    }
}
