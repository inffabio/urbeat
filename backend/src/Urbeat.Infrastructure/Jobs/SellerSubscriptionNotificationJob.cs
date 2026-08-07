using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Jobs;

public sealed class SellerSubscriptionNotificationJob
{
    private readonly ISubscriptionNotificationService _subscriptionNotificationService;
    private readonly ILogger<SellerSubscriptionNotificationJob> _logger;

    public SellerSubscriptionNotificationJob(
        ISubscriptionNotificationService subscriptionNotificationService,
        ILogger<SellerSubscriptionNotificationJob> logger)
    {
        _subscriptionNotificationService = subscriptionNotificationService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        await _subscriptionNotificationService.ProcessSellerSubscriptionNotificationsAsync();
        _logger.LogInformation("Seller subscription notification job executed at {ExecutedAtUtc}", DateTime.UtcNow);
    }
}
