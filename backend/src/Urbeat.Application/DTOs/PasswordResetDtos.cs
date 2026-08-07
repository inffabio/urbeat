namespace Urbeat.Application.DTOs;

public sealed class ForgotPasswordRequestDto
{
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequestDto
{
    public string Token { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed class ValidateResetTokenResponseDto
{
    public bool Valid { get; init; }
    public string? Message { get; init; }
}
