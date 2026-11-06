namespace Urbeat.Application.DTOs;

public sealed class ForgotPasswordRequestDto
{
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Login context being recovered: "Customer" or "Seller". A token issued for one role can
    /// never change the other role's credential.
    /// </summary>
    public string Role { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequestDto
{
    public string Token { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
    public string ConfirmPassword { get; init; } = string.Empty;

    /// <summary>
    /// Optional context echoed by the client. When supplied it must match the role bound to the
    /// token; the token role is authoritative and legacy tokens without a role are rejected.
    /// </summary>
    public string? Role { get; init; }
}

public sealed class ValidateResetTokenResponseDto
{
    public bool Valid { get; init; }
    public string? Message { get; init; }
    public string? Role { get; init; }
}

public sealed class ChangePasswordRequestDto
{
    /// <summary>Login context being changed: "Customer" or "Seller".</summary>
    public string Role { get; init; } = string.Empty;

    public string CurrentPassword { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
    public string ConfirmPassword { get; init; } = string.Empty;
}
