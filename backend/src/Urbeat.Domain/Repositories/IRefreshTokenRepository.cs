using Urbeat.Domain.Entities;

namespace Urbeat.Domain.Repositories;

/// <summary>
/// Owner and login context of a claimed refresh token. <see cref="Role"/> is null only for
/// legacy tokens issued before role-aware sessions.
/// </summary>
public sealed record RefreshTokenClaim(string UserId, string? Role);

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims a refresh token for one-time use by revoking it. Returns the owning
    /// user id and login context when exactly one active (non-expired, non-revoked) token
    /// matched the given hash; otherwise returns null. The relational implementation uses a
    /// single conditional UPDATE so the claim is safe across concurrent requests and replicas.
    /// </summary>
    Task<RefreshTokenClaim?> TryClaimAsync(string tokenHash, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every active (non-revoked) refresh token belonging to the user. Used after a
    /// credential change such as a password reset so previously issued sessions cannot be renewed.
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes active refresh tokens for a single login context. Resetting the Seller password
    /// must not end the Customer session (and vice versa).
    /// </summary>
    Task RevokeAllForUserAndRoleAsync(Guid userId, string role, DateTime utcNow, CancellationToken cancellationToken = default);
}
