using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Security;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class RefreshTokenRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly RefreshTokenRepository _sut;

    public RefreshTokenRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-refresh-token-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new RefreshTokenRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task TryClaimAsync_ShouldReturnUserIdOnce_AndRejectReplay()
    {
        var userId = Guid.NewGuid();
        const string raw = "raw-refresh-token";
        var hash = RefreshTokenHasher.Hash(raw);

        await _sut.AddAsync(new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync();

        var first = await _sut.TryClaimAsync(hash, DateTime.UtcNow, CancellationToken.None);
        var replay = await _sut.TryClaimAsync(hash, DateTime.UtcNow, CancellationToken.None);

        first.Should().Be(userId.ToString());
        replay.Should().BeNull();
        (await _db.RefreshTokens.SingleAsync()).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task TryClaimAsync_ShouldReturnNull_WhenTokenIsExpired()
    {
        var hash = RefreshTokenHasher.Hash("expired");
        await _sut.AddAsync(new RefreshToken
        {
            UserId = Guid.NewGuid(),
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await _db.SaveChangesAsync();

        var result = await _sut.TryClaimAsync(hash, DateTime.UtcNow, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimAsync_ShouldReturnNull_WhenTokenIsAlreadyRevoked()
    {
        var hash = RefreshTokenHasher.Hash("revoked");
        await _sut.AddAsync(new RefreshToken
        {
            UserId = Guid.NewGuid(),
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            RevokedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        await _db.SaveChangesAsync();

        var result = await _sut.TryClaimAsync(hash, DateTime.UtcNow, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimAsync_ShouldReturnNull_WhenTokenHashIsUnknown()
    {
        var result = await _sut.TryClaimAsync(RefreshTokenHasher.Hash("unknown"), DateTime.UtcNow, CancellationToken.None);

        result.Should().BeNull();
    }
}
