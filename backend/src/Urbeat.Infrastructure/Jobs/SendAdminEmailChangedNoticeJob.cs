using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Hangfire;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.Infrastructure.Jobs;

/// <summary>
/// Notifies both addresses after an active administrator's e-mail is changed. The address change is
/// already committed when this job runs, so a delivery failure never rolls it back. Failures are
/// collected and rethrown so Hangfire retries the whole notice (delivery is at-least-once; the
/// duplicate-address case is harmless and the retry is what makes the security warning reliable).
/// </summary>
[Queue(HangfireQueues.Communications)]
public sealed class SendAdminEmailChangedNoticeJob
{
    private readonly IEmailService _emailService;
    private readonly EmailConfirmationOptions _options;
    private readonly ILogger<SendAdminEmailChangedNoticeJob> _logger;

    public SendAdminEmailChangedNoticeJob(
        IEmailService emailService,
        IOptions<EmailConfirmationOptions> options,
        ILogger<SendAdminEmailChangedNoticeJob> logger)
    {
        _emailService = emailService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(string oldEmail, string newEmail)
    {
        var loginUrl = $"{_options.FrontendBaseUrl.TrimEnd('/')}/painel/login";
        var (oldSubject, oldHtml) = EmailTemplates.BuildAdminEmailChangedToOldAddress(newEmail, loginUrl);
        var (newSubject, newHtml) = EmailTemplates.BuildAdminEmailChangedToNewAddress(oldEmail, loginUrl);

        var failures = new List<Exception>();
        await SendBestEffortAsync(oldEmail, oldSubject, oldHtml, "old", failures);
        await SendBestEffortAsync(newEmail, newSubject, newHtml, "new", failures);

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "One or more administrative e-mail change notices could not be delivered.",
                failures);
        }

        _logger.LogInformation(
            "{EventType} | Administrative e-mail change notices processed",
            "ADMIN_EMAIL_CHANGE_NOTICE_SENT");
    }

    private async Task SendBestEffortAsync(
        string address,
        string subject,
        string html,
        string target,
        List<Exception> failures)
    {
        try
        {
            await _emailService.SendAsync(
                address,
                address,
                subject,
                html,
                textBody: null,
                idempotencyKey: null,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            _logger.LogWarning(
                exception,
                "{EventType} | Administrative e-mail change notice failed | Target={Target}",
                "ADMIN_EMAIL_CHANGE_NOTICE_FAILED",
                target);
        }
    }
}
