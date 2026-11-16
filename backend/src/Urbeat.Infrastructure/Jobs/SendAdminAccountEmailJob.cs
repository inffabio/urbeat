using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Hangfire;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.Infrastructure.Jobs;

/// <summary>
/// Delivers an administrative invitation or password reset link. The payload carries only the
/// invitation id: the token is rebuilt from the persisted row and the signing key, so the job
/// survives restarts and a lost enqueue never strands a pending link. Neither the token nor the
/// e-mail address is logged.
/// </summary>
[Queue(HangfireQueues.Communications)]
public sealed class SendAdminAccountEmailJob
{
    private readonly IAdminInvitationService _invitationService;
    private readonly IEmailService _emailService;
    private readonly EmailConfirmationOptions _options;
    private readonly AdminAccountOptions _adminOptions;
    private readonly ILogger<SendAdminAccountEmailJob> _logger;

    public SendAdminAccountEmailJob(
        IAdminInvitationService invitationService,
        IEmailService emailService,
        IOptions<EmailConfirmationOptions> options,
        IOptions<AdminAccountOptions> adminOptions,
        ILogger<SendAdminAccountEmailJob> logger)
    {
        _invitationService = invitationService;
        _emailService = emailService;
        _options = options.Value;
        _adminOptions = adminOptions.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid invitationId)
    {
        var delivery = await _invitationService.TryClaimEmailDeliveryAsync(
            invitationId,
            CancellationToken.None);

        if (delivery is null)
        {
            _logger.LogInformation(
                "{EventType} | Administrative invitation is no longer sendable or is being delivered | InvitationId={InvitationId}",
                "ADMIN_INVITATION_EMAIL_SKIPPED",
                invitationId);
            return;
        }

        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        var path = delivery.Purpose == AdminInvitationPurpose.PasswordReset
            ? _adminOptions.PasswordResetPath
            : _adminOptions.InvitationPath;

        // The token travels only in the fragment, which is never sent to the server, Referer or a
        // proxy access log. The page strips it immediately with replaceState.
        var url = $"{baseUrl}{path}#token={Uri.EscapeDataString(delivery.Token)}";

        var (subject, html) = delivery.Purpose == AdminInvitationPurpose.PasswordReset
            ? EmailTemplates.BuildAdminPasswordReset(
                url,
                _adminOptions.PasswordResetLifetimeMinutes)
            : EmailTemplates.BuildAdminInvitation(
                url,
                _adminOptions.InvitationLifetimeHours);

        try
        {
            await _emailService.SendAsync(
                delivery.Email,
                delivery.Email,
                subject,
                html,
                textBody: null,
                idempotencyKey: CreateMessageIdempotencyKey(
                    delivery.Purpose,
                    delivery.InvitationId,
                    delivery.AttemptId),
                cancellationToken: CancellationToken.None);

            await _invitationService.MarkEmailSentAsync(
                delivery.InvitationId,
                delivery.AttemptId,
                CancellationToken.None);

            _logger.LogInformation(
                "{EventType} | Administrative invitation e-mail sent | InvitationId={InvitationId} | Purpose={Purpose}",
                "ADMIN_INVITATION_EMAIL_SENT",
                delivery.InvitationId,
                delivery.Purpose);
        }
        catch (Exception exception)
        {
            await ReleaseLeaseBestEffortAsync(delivery.InvitationId, delivery.AttemptId);

            _logger.LogError(
                exception,
                "{EventType} | Administrative invitation e-mail failed | InvitationId={InvitationId} | Purpose={Purpose}",
                "ADMIN_INVITATION_EMAIL_FAILED",
                delivery.InvitationId,
                delivery.Purpose);
            throw;
        }
    }

    /// <summary>
    /// Valid RFC 5322 dot-atom identity of the message. It contains only the purpose, the
    /// invitation id and the lease attempt, never the token or the address.
    /// </summary>
    internal static string CreateMessageIdempotencyKey(
        AdminInvitationPurpose purpose,
        Guid invitationId,
        Guid attemptId)
        => $"admin-{(purpose == AdminInvitationPurpose.PasswordReset ? "password-reset" : "invitation")}-{invitationId:N}-{attemptId:N}";

    private async Task ReleaseLeaseBestEffortAsync(Guid invitationId, Guid attemptId)
    {
        try
        {
            await _invitationService.ReleaseEmailSendLeaseAsync(
                invitationId,
                attemptId,
                CancellationToken.None);
        }
        catch (Exception releaseException)
        {
            _logger.LogWarning(
                releaseException,
                "{EventType} | Could not release the administrative invitation email lease | InvitationId={InvitationId}",
                "ADMIN_INVITATION_EMAIL_LEASE_RELEASE_FAILED",
                invitationId);
        }
    }
}
