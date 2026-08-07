using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IEmailConfirmationService
{
    Task SendConfirmationEmailAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<EmailConfirmationResultDto> ConfirmAsync(ConfirmEmailRequestDto request, CancellationToken cancellationToken = default);

    Task<EmailConfirmationResultDto> ConfirmByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);

    Task<EmailConfirmationResultDto> ResendAsync(ResendEmailConfirmationRequestDto request, CancellationToken cancellationToken = default);
}
