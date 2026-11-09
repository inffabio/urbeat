using Hangfire;
using Urbeat.Infrastructure.Jobs;

namespace Urbeat.Infrastructure.Hangfire;

/// <summary>
/// Registers the recurring jobs owned by the business worker. Only one logical scheduler role may
/// call this method; <see cref="TryRegisterBusinessJobs"/> returns <c>false</c> for every other role.
/// </summary>
public static class HangfireRecurringJobs
{
    public const string OperationalHeartbeatJobId = "operational-heartbeat";
    public const string SellerSubscriptionNotificationJobId = "seller-subscription-notifications";

    public static bool IsRecurringScheduler(HangfireRole role) =>
        role is HangfireRole.Business or HangfireRole.All;

    /// <summary>
    /// Upserts the recurring jobs when <paramref name="role"/> is the designated scheduler.
    /// Hangfire's <c>AddOrUpdate</c> is idempotent by recurring-job id, so restarting the business
    /// worker (or scaling more than one business instance) never creates duplicate definitions.
    /// </summary>
    public static bool TryRegisterBusinessJobs(IRecurringJobManager manager, HangfireRole role)
    {
        ArgumentNullException.ThrowIfNull(manager);

        if (!IsRecurringScheduler(role))
        {
            return false;
        }

#pragma warning disable CS0618 // QueueName remains the supported way to pin a recurring job queue in Hangfire 1.8.
        manager.AddOrUpdate<OperationalHeartbeatJob>(
            OperationalHeartbeatJobId,
            job => job.ExecuteAsync(),
            Cron.Hourly,
            new RecurringJobOptions { QueueName = HangfireQueues.Business });

        manager.AddOrUpdate<SellerSubscriptionNotificationJob>(
            SellerSubscriptionNotificationJobId,
            job => job.ExecuteAsync(),
            Cron.Daily,
            new RecurringJobOptions { QueueName = HangfireQueues.Business });
#pragma warning restore CS0618

        return true;
    }
}
