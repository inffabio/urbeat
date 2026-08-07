namespace Urbeat.Application.Interfaces;

public interface ISubscriptionNotificationService
{
    Task ProcessSellerSubscriptionNotificationsAsync(CancellationToken cancellationToken = default);
}
