using Hangfire;
using Microsoft.Extensions.Logging;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Hangfire;
using Urbeat.Infrastructure.Identity;

namespace Urbeat.Infrastructure.Jobs;

/// <summary>
/// Recovery sweep for role promotion e-mails. A pending request is durable, but its Hangfire
/// enqueue happens after the database commit; a crash in between (or a lost job) used to leave the
/// request without any way to reach the owner. This job re-enqueues every still-sendable request
/// that has no recorded delivery and no active delivery lease, so the link is always produced while
/// the challenge is valid.
/// </summary>
/// <remarks>
/// It runs on the business queue (it is a scheduling sweep) and enqueues the actual delivery on the
/// communications queue. Re-enqueueing is safe: the delivery job claims a persisted lease, so a
/// request that is still being sent (or was sent after this sweep read the database) is skipped.
/// </remarks>
[Queue(HangfireQueues.Business)]
public sealed class RolePromotionEmailRecoveryJob
{
    /// <summary>
    /// Grace period before a request is considered stranded. It gives the immediate enqueue from the
    /// registration request time to run, avoiding a duplicate send on the happy path.
    /// </summary>
    public static readonly TimeSpan RecoveryGracePeriod = TimeSpan.FromMinutes(1);

    private readonly IRolePromotionChallengeService _challengeService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<RolePromotionEmailRecoveryJob> _logger;

    public RolePromotionEmailRecoveryJob(
        IRolePromotionChallengeService challengeService,
        IBackgroundJobClient backgroundJobClient,
        ILogger<RolePromotionEmailRecoveryJob> logger)
    {
        _challengeService = challengeService;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var requestIds = await _challengeService.FindPendingEmailRequestIdsAsync(
            RecoveryGracePeriod,
            RolePromotionChallengeService.EmailSendLease,
            CancellationToken.None);

        foreach (var requestId in requestIds)
        {
            // The id is the only payload; the job re-derives the token from the persisted request.
            _backgroundJobClient.Enqueue<SendRolePromotionEmailJob>(job => job.ExecuteAsync(requestId));
        }

        if (requestIds.Count > 0)
        {
            _logger.LogInformation(
                "{EventType} | Re-enqueued role promotion emails | Count={Count}",
                "ROLE_PROMOTION_EMAIL_RECOVERY_ENQUEUED",
                requestIds.Count);
        }
    }
}
