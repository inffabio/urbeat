using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Jobs;

public sealed class SendEmailConfirmationJob
{
    private readonly IEmailConfirmationService _emailConfirmationService;
    private readonly ILogger<SendEmailConfirmationJob> _logger;

    public SendEmailConfirmationJob(
        IEmailConfirmationService emailConfirmationService,
        ILogger<SendEmailConfirmationJob> logger)
    {
        _emailConfirmationService = emailConfirmationService;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid userId)
    {
        _logger.LogInformation("{EventType} | Sending confirmation email | UserId={UserId}",
            "EMAIL_CONFIRM_JOB_STARTED", userId);
        await _emailConfirmationService.SendConfirmationEmailAsync(userId, CancellationToken.None);
        _logger.LogInformation("{EventType} | Job completed | UserId={UserId}",
            "EMAIL_CONFIRM_JOB_COMPLETED", userId);
    }
}
