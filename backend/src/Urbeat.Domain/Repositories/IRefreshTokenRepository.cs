using Urbeat.Domain.Entities;

namespace Urbeat.Domain.Repositories;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims a refresh token for one-time use by revoking it. Returns the owning
    /// user id when exactly one active (non-expired, non-revoked) token matched the given hash;
    /// otherwise returns null. The relational implementation uses a single conditional UPDATE so
    /// the claim is safe across concurrent requests and replicas.
    /// </summary>
    Task<string?> TryClaimAsync(string tokenHash, DateTime utcNow, CancellationToken cancellationToken = default);
}
