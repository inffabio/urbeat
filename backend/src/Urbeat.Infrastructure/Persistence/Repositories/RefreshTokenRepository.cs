using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _dbContext;

    public RefreshTokenRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        await _dbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken);
    }

    public async Task<string?> TryClaimAsync(string tokenHash, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsRelational())
        {
            // A single atomic conditional UPDATE doubles as the use-once guard: only one
            // concurrent request can flip RevokedAtUtc for a given token hash. This is the
            // source of truth across processes/replicas.
            var affected = await _dbContext.RefreshTokens
                .Where(x => x.TokenHash == tokenHash && x.RevokedAtUtc == null && x.ExpiresAtUtc > utcNow)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.RevokedAtUtc, utcNow),
                    cancellationToken);

            if (affected == 0)
            {
                return null;
            }

            return await _dbContext.RefreshTokens
                .AsNoTracking()
                .Where(x => x.TokenHash == tokenHash)
                .Select(x => x.UserId.ToString())
                .SingleAsync(cancellationToken);
        }

        // InMemory (unit/integration tests): best-effort claim, not concurrency-safe. Production
        // relies on the relational path above.
        var token = await _dbContext.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (token is null || token.IsRevoked || token.IsExpired)
        {
            return null;
        }

        token.RevokedAtUtc = utcNow;
        token.MarkAsUpdated();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return token.UserId.ToString();
    }

    public async Task RevokeAllForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.RefreshTokens
                .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.RevokedAtUtc, utcNow),
                    cancellationToken);
            return;
        }

        var tokens = await _dbContext.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = utcNow;
            token.MarkAsUpdated();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
