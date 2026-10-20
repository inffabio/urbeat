using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface INotificationService
{
    Task<CustomerNotificationsResponseDto> ListCustomerNotificationsAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default);

    Task<SellerNotificationsResponseDto> ListSellerNotificationsAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default);

    Task NotifySellerNewOrderAsync(
        Guid sellerUserId,
        Guid orderId,
        string message,
        CancellationToken cancellationToken = default);

    Task NotifyCustomerOrderStatusChangedAsync(
        Guid customerUserId,
        Guid orderId,
        Domain.Entities.OrderStatus status,
        string? message,
        CancellationToken cancellationToken = default);

    Task NotifySellerSubscriptionStatusAsync(
        Guid sellerUserId,
        Guid subscriptionReferenceId,
        Domain.Entities.NotificationType notificationType,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes a live SignalR notification. Returns <c>false</c> only when a real delivery was
    /// attempted and failed; callers (outbox handlers) use that signal to schedule a retry instead
    /// of silently dropping the live event. A missing hub context returns <c>true</c> because there
    /// is no delivery channel to fail (durable notifications remain the fallback).
    /// </summary>
    Task<bool> PushSellerNotificationAsync(
        Guid sellerUserId,
        Domain.Entities.Notification notification,
        CancellationToken cancellationToken = default);

    Task<bool> PushCustomerNotificationAsync(
        Guid customerUserId,
        Domain.Entities.Notification notification,
        CancellationToken cancellationToken = default);

    Task<bool> NotifyCustomerOrderStatusUpdatedAsync(
        Guid customerUserId,
        Guid orderId,
        string orderCode,
        Domain.Entities.OrderStatus status,
        DateTime changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the seller acceptance event used to trigger automatic printing. Delivery is
    /// best-effort/at-least-once; callers must tolerate duplicates and reconcile through the
    /// authoritative store orders endpoint.
    /// </summary>
    Task<bool> NotifySellerOrderAcceptedForPrintingAsync(
        Guid sellerUserId,
        Guid orderId,
        Guid storeId,
        string orderCode,
        int sequence,
        DateTime acceptedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> MarkAsReadAsync(Guid notificationId, Guid recipientUserId, CancellationToken cancellationToken = default);
}
