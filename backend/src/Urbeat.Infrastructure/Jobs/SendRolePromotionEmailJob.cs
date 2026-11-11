using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Hangfire;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.Infrastructure.Jobs;

/// <summary>
/// Sends the role promotion confirmation e-mail. The durable job payload contains only the request
/// id: the link is re-derived from the persisted request and the server signing key, so the job
/// still works after a restart, a late execution or a cache loss. Neither the token nor the
/// password hash is ever logged or placed in the payload.
/// </summary>
[Queue(HangfireQueues.Communications)]
public sealed class SendRolePromotionEmailJob
{
    private readonly IRolePromotionChallengeService _challengeService;
    private readonly IEmailService _emailService;
    private readonly EmailConfirmationOptions _options;
    private readonly ILogger<SendRolePromotionEmailJob> _logger;

    public SendRolePromotionEmailJob(
        IRolePromotionChallengeService challengeService,
        IEmailService emailService,
        IOptions<EmailConfirmationOptions> options,
        ILogger<SendRolePromotionEmailJob> logger)
    {
        _challengeService = challengeService;
        _emailService = emailService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid requestId)
    {
        _logger.LogInformation(
            "{EventType} | Sending role promotion email | RequestId={RequestId}",
            "ROLE_PROMOTION_EMAIL_JOB_STARTED",
            requestId);

        var delivery = await _challengeService.TryClaimEmailDeliveryAsync(requestId, CancellationToken.None);
        if (delivery is null)
        {
            // Already sent, applied, superseded, expired, or another attempt holds a valid lease:
            // nothing to deliver. The request row is the single source of truth, so there is no
            // cache entry to clean up.
            _logger.LogInformation(
                "{EventType} | Promotion request is no longer sendable or is being delivered | RequestId={RequestId}",
                "ROLE_PROMOTION_EMAIL_SKIPPED",
                requestId);
            return;
        }

        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(_options.RolePromotionPath)
            ? "/confirmacao-email"
            : _options.RolePromotionPath;
        var roleName = delivery.TargetRole.ToString();
        // The token travels in the URL fragment, never in the query string: a fragment is not sent
        // to the server, to a Referer header, or to a proxy access log. The frontend reads it once,
        // strips it with replaceState and keeps it only in memory before the confirmation POST.
        var confirmUrl = $"{baseUrl}{path}#promotionToken={Uri.EscapeDataString(delivery.Token)}&role={Uri.EscapeDataString(roleName)}";

        var (subject, html) = EmailTemplates.BuildRolePromotionConfirmation(
            delivery.TargetRole,
            confirmUrl,
            expiresMinutes: (int)RolePromotionChallengeService.ChallengeTtl.TotalMinutes);

        try
        {
            await _emailService.SendAsync(
                delivery.Email,
                delivery.Email,
                subject,
                html,
                textBody: null,
                // Deterministic per request id (never the token or password). SmtpEmailService uses
                // it as the outbound Message-Id so a retry carries the same identity and an MTA or
                // provider can deduplicate it. A new GUID per attempt would defeat that.
                idempotencyKey: CreateMessageIdempotencyKey(requestId),
                cancellationToken: CancellationToken.None);

            // SMTP delivery is inherently at-least-once: if this process dies after the provider
            // accepted the message but before the line below, the lease expires and recovery sends
            // the same link again. The deterministic Message-Id is what reduces that unavoidable
            // duplicate; the persisted lease prevents the avoidable ones (duplicate/late enqueue
            // while another attempt is sending).
            await _challengeService.MarkEmailSentAsync(
                requestId,
                delivery.AttemptId,
                CancellationToken.None);

            _logger.LogInformation(
                "{EventType} | Role promotion email sent | RequestId={RequestId} | Role={Role}",
                "ROLE_PROMOTION_EMAIL_SENT",
                requestId,
                delivery.TargetRole);
        }
        catch (Exception exception)
        {
            // Release the lease so a retry can claim the request immediately instead of waiting for
            // the lease to expire. Never log the token, URL or password.
            await ReleaseLeaseBestEffortAsync(requestId, delivery.AttemptId);

            _logger.LogError(
                exception,
                "{EventType} | Role promotion email failed | RequestId={RequestId}",
                "ROLE_PROMOTION_EMAIL_FAILED",
                requestId);
            throw;
        }
    }

    /// <summary>
    /// Stable identity of the promotion message for a request. It is derived only from the request
    /// id, so the same request always maps to the same Message-Id across retries. The value is the
    /// left part of the Message-Id (the transport appends the domain) and must stay a valid
    /// RFC 5322 dot-atom: no colon, whitespace or other specials, or the header would be invalid.
    /// </summary>
    internal static string CreateMessageIdempotencyKey(Guid requestId) => $"role-promotion-{requestId:N}";

    private async Task ReleaseLeaseBestEffortAsync(Guid requestId, Guid attemptId)
    {
        try
        {
            await _challengeService.ReleaseEmailSendLeaseAsync(
                requestId,
                attemptId,
                CancellationToken.None);
        }
        catch (Exception releaseException)
        {
            _logger.LogWarning(
                releaseException,
                "{EventType} | Could not release the role promotion email lease | RequestId={RequestId}",
                "ROLE_PROMOTION_EMAIL_LEASE_RELEASE_FAILED",
                requestId);
        }
    }
}
