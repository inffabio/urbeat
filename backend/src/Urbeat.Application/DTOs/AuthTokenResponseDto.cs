namespace Urbeat.Application.DTOs;

public sealed class AuthTokenResponseDto
{
    public string AccessToken { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }
}
