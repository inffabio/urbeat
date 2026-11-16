namespace Urbeat.Application.DTOs;

public sealed class LoginResultDto
{
    public bool Succeeded { get; init; }

    public bool IsLockedOut { get; init; }

    public bool IsForbidden { get; init; }

    public bool IsEmailNotConfirmed { get; init; }

    /// <summary>Set when the administrator was blocked by another administrator.</summary>
    public bool IsBlocked { get; init; }

    public string? Error { get; init; }

    public AuthTokenResponseDto? Token { get; init; }

    public string? RefreshToken { get; init; }

    public DateTime? RefreshTokenExpiresAtUtc { get; init; }
}
