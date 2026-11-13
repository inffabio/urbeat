namespace Urbeat.Application.DTOs;

/// <summary>
/// Break-glass request to set a new password for the seeded administrator without knowing the
/// current one. Authorization is the dedicated recovery key header, never this body.
/// </summary>
public sealed class AdminRecoverPasswordRequestDto
{
    public string Email { get; init; } = string.Empty;

    public string NewPassword { get; init; } = string.Empty;

    public string ConfirmPassword { get; init; } = string.Empty;
}

public enum AdminPasswordRecoveryStatus
{
    Succeeded,
    /// <summary>The feature is disabled or the recovery key is not configured.</summary>
    NotAvailable,
    /// <summary>The header is missing, malformed or does not match the configured key.</summary>
    InvalidKey,
    /// <summary>The key was already consumed by a previous successful recovery.</summary>
    KeyAlreadyUsed,
    /// <summary>The target is not an allowed administrator account.</summary>
    TargetRejected,
    /// <summary>The new password failed Identity validation.</summary>
    PasswordRejected,
    /// <summary>An unexpected persistence/provider failure occurred.</summary>
    Failed
}

public sealed class AdminPasswordRecoveryResultDto
{
    public AdminPasswordRecoveryStatus Status { get; init; }

    /// <summary>Sanitized message only; never contains the password, key or token.</summary>
    public string? Error { get; init; }
}
