using System.Text;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services.Email;

public sealed class EmailConfirmationService : IEmailConfirmationService
{
    private const string CustomerRole = "Customer";

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IEmailService _emailService;
    private readonly EmailConfirmationOptions _options;
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailTokenCache _emailTokenCache;
    private readonly ILogger<EmailConfirmationService> _logger;

    public EmailConfirmationService(
        UserManager<IdentityUser<Guid>> userManager,
        IEmailService emailService,
        IOptions<EmailConfirmationOptions> options,
        ApplicationDbContext dbContext,
        IEmailTokenCache emailTokenCache,
        ILogger<EmailConfirmationService> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _options = options.Value;
        _dbContext = dbContext;
        _emailTokenCache = emailTokenCache;
        _logger = logger;
    }

    public async Task SendConfirmationEmailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            _logger.LogWarning("{EventType} | User not found | UserId={UserId}", "EMAIL_CONFIRM_USER_NOT_FOUND", userId);
            return;
        }

        if (user.EmailConfirmed)
        {
            _logger.LogInformation("{EventType} | Already confirmed | UserId={UserId}", "EMAIL_ALREADY_CONFIRMED", userId);
            return;
        }

        var rawToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));

        var shortCode = RedisEmailTokenCache.GenerateCode();
        await _emailTokenCache.SetMappingAsync(shortCode, user.Id, encodedToken, cancellationToken);
        var confirmUrl = BuildConfirmUrl(shortCode);

        var roles = await _userManager.GetRolesAsync(user);
        var isCustomer = roles.Contains(CustomerRole, StringComparer.OrdinalIgnoreCase);

        var (subject, html) = isCustomer
            ? EmailTemplates.BuildCustomerConfirmation(confirmUrl)
            : EmailTemplates.BuildSellerConfirmation(confirmUrl);

        try
        {
            await _emailService.SendAsync(
                toAddress: user.Email ?? string.Empty,
                toName: user.UserName ?? user.Email ?? string.Empty,
                subject: subject,
                htmlBody: html,
                cancellationToken: cancellationToken);

            await WriteAuditLogAsync(user.Id, "EmailConfirmationSent",
                $"Confirmation e-mail sent to {user.Email}.", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{EventType} | Failed to send confirmation email | UserId={UserId}",
                "EMAIL_CONFIRM_SEND_FAILED", user.Id);
            await WriteAuditLogAsync(user.Id, "EmailConfirmationSendFailed",
                $"Failed to send confirmation e-mail to {user.Email}: {ex.Message}",
                cancellationToken);
        }
    }

    public async Task<EmailConfirmationResultDto> ConfirmByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var mapping = await _emailTokenCache.GetMappingAsync(shortCode, cancellationToken);
        if (mapping is null)
        {
            return new EmailConfirmationResultDto { InvalidToken = true, Errors = ["Link de confirmação inválido ou expirado."] };
        }

        var request = new ConfirmEmailRequestDto { UserId = mapping.UserId, Token = mapping.Token };
        return await ConfirmAsync(request, cancellationToken);
    }

    public async Task<EmailConfirmationResultDto> ConfirmAsync(ConfirmEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return new EmailConfirmationResultDto { UserNotFound = true };
        }

        if (user.EmailConfirmed)
        {
            return new EmailConfirmationResultDto { Succeeded = true, AlreadyConfirmed = true };
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch
        {
            return new EmailConfirmationResultDto { InvalidToken = true, Errors = ["Invalid token format."] };
        }

        var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
        if (!result.Succeeded)
        {
            await WriteAuditLogAsync(user.Id, "EmailConfirmationFailed",
                $"Confirmation failed for {user.Email}: {string.Join("; ", result.Errors.Select(e => e.Description))}",
                cancellationToken);
            return new EmailConfirmationResultDto
            {
                InvalidToken = true,
                Errors = result.Errors.Select(x => x.Description).ToArray(),
            };
        }

        await WriteAuditLogAsync(user.Id, "EmailConfirmed",
            $"E-mail confirmed for {user.Email}.", cancellationToken);

        _logger.LogInformation("{EventType} | Email confirmed | UserId={UserId} | Email={Email}",
            "EMAIL_CONFIRMED", user.Id, user.Email);

        return new EmailConfirmationResultDto { Succeeded = true };
    }

    public async Task<EmailConfirmationResultDto> ResendAsync(ResendEmailConfirmationRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(normalizedEmail);

        // For privacy, we always return success — do not reveal whether the e-mail exists.
        if (user is null)
        {
            _logger.LogInformation("{EventType} | Resend skipped (user not found) | Email={Email}",
                "EMAIL_RESEND_SKIPPED", normalizedEmail);
            return new EmailConfirmationResultDto { Succeeded = true };
        }

        if (user.EmailConfirmed)
        {
            return new EmailConfirmationResultDto { Succeeded = true, AlreadyConfirmed = true };
        }

        await SendConfirmationEmailAsync(user.Id, cancellationToken);
        await WriteAuditLogAsync(user.Id, "EmailConfirmationResent",
            $"Confirmation e-mail resent to {user.Email}.", cancellationToken);

        return new EmailConfirmationResultDto { Succeeded = true };
    }

    private string BuildConfirmUrl(string shortCode)
    {
        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        return $"{baseUrl}/c/{shortCode}";
    }

    private async Task WriteAuditLogAsync(Guid userId, string auditEvent, string description, CancellationToken cancellationToken)
    {
        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = userId,
            Event = auditEvent,
            Entity = nameof(IdentityUser<Guid>),
            EntityId = userId,
            Description = description,
            IpAddress = null,
        }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
