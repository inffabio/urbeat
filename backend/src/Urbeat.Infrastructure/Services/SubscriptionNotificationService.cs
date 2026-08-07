using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class SubscriptionNotificationService : ISubscriptionNotificationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IEfUnitOfWork _efUnitOfWork;

    public SubscriptionNotificationService(
        ApplicationDbContext dbContext,
        INotificationService notificationService,
        IEfUnitOfWork efUnitOfWork)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _efUnitOfWork = efUnitOfWork;
    }

    public async Task ProcessSellerSubscriptionNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var soonThreshold = now.AddDays(3);

        var items = await _dbContext.SellerSubscriptionStatuses
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            var shouldBlockStore = item.BillingStatus is SellerSubscriptionBillingStatus.Overdue or SellerSubscriptionBillingStatus.Blocked
                || item.NextDueDateUtc <= now;

            var store = await _dbContext.Stores
                .SingleOrDefaultAsync(x => x.OwnerUserId == item.SellerUserId, cancellationToken);

            if (store is not null)
            {
                store.IsSubscriptionBlocked = shouldBlockStore;

                if (shouldBlockStore)
                {
                    store.IsOpen = false;
                }

                store.MarkAsUpdated();
            }

            if (item.BillingStatus == SellerSubscriptionBillingStatus.Blocked)
            {
                await _notificationService.NotifySellerSubscriptionStatusAsync(
                    item.SellerUserId,
                    item.Id,
                    NotificationType.StoreBlockedBySubscription,
                    $"Sua loja foi bloqueada por inadimplencia. Vencimento em {item.NextDueDateUtc:yyyy-MM-dd}.",
                    cancellationToken);

                item.LastNotifiedAtUtc = now;
                item.MarkAsUpdated();

                continue;
            }

            if (item.BillingStatus == SellerSubscriptionBillingStatus.Overdue || item.NextDueDateUtc <= now)
            {
                await _notificationService.NotifySellerSubscriptionStatusAsync(
                    item.SellerUserId,
                    item.Id,
                    NotificationType.SubscriptionOverdue,
                    $"Sua assinatura esta vencida desde {item.NextDueDateUtc:yyyy-MM-dd}. Regularize para evitar bloqueio.",
                    cancellationToken);

                item.LastNotifiedAtUtc = now;
                item.MarkAsUpdated();

                continue;
            }

            if (item.NextDueDateUtc <= soonThreshold)
            {
                await _notificationService.NotifySellerSubscriptionStatusAsync(
                    item.SellerUserId,
                    item.Id,
                    NotificationType.SubscriptionDueSoon,
                    $"Sua assinatura vence em {item.NextDueDateUtc:yyyy-MM-dd}.",
                    cancellationToken);

                item.LastNotifiedAtUtc = now;
                item.MarkAsUpdated();
            }
        }

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
    }
}
