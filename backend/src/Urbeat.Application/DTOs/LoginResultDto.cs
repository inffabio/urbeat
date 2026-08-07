namespace Urbeat.Application.DTOs;

public sealed class LoginResultDto
{
    public bool Succeeded { get; init; }

    public bool IsLockedOut { get; init; }

    public bool IsForbidden { get; init; }

    public bool IsEmailNotConfirmed { get; init; }

    public string? Error { get; init; }

    public AuthTokenResponseDto? Token { get; init; }
}