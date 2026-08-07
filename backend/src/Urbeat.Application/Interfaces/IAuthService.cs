using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IAuthService
{
    Task<RegistrationResultDto> RegisterCustomerAsync(RegisterUserRequestDto request, CancellationToken cancellationToken = default);

    Task<RegistrationResultDto> RegisterSellerAsync(RegisterUserRequestDto request, CancellationToken cancellationToken = default);

    Task<LoginResultDto> LoginAsync(
        LoginRequestDto request,
        string requiredRole,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AuthTokenResponseDto?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<bool> ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken = default);

    Task<ValidateResetTokenResponseDto> ValidateResetTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? Error)> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? Error)> UpdateEmailAsync(Guid userId, UpdateEmailRequestDto request, CancellationToken cancellationToken = default);
}