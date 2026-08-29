namespace Urbeat.Application.DTOs;

/// <summary>
/// Internal token material produced by the token service. This is never returned to a client
/// as-is: the access token is exposed via <see cref="AuthTokenResponseDto"/> while the refresh
/// token is only written into an HttpOnly cookie and persisted for rotation.
/// </summary>
public sealed class AuthTokenPairDto
{
    public string AccessToken { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }

    public string RefreshToken { get; init; } = string.Empty;

    public DateTime RefreshTokenExpiresAtUtc { get; init; }
}
