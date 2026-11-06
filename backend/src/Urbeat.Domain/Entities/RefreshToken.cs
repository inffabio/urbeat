namespace Urbeat.Domain.Entities;

public sealed class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Login context that issued this refresh token. A renewed access token must keep the
    /// original role so Customer and Seller sessions never mix. Null on legacy tokens, which
    /// keep the old behavior until they expire.
    /// </summary>
    public string? Role { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
}
