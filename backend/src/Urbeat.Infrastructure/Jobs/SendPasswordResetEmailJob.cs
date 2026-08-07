using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Services.Email;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Jobs;

public sealed class SendPasswordResetEmailJob
{
    private readonly IEmailService _emailService;
    private readonly ILogger<SendPasswordResetEmailJob> _logger;

    public SendPasswordResetEmailJob(IEmailService emailService, ILogger<SendPasswordResetEmailJob> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task ExecuteAsync(string email, string userName, string resetLink)
    {
        _logger.LogInformation("Password reset email enqueued for {Email}", email);
        var (subject, htmlBody) = EmailTemplates.BuildPasswordReset(userName, resetLink);
        await _emailService.SendAsync(toAddress: email, toName: userName, subject: subject, htmlBody: htmlBody);
        _logger.LogInformation("Password reset email sent to {Email}", email);
    }
}
