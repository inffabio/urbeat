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

    Task<bool> MarkAsReadAsync(Guid notificationId, Guid recipientUserId, CancellationToken cancellationToken = default);
}
