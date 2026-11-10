using Urbeat.Infrastructure.Caching;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.WebApi.Operations;

/// <summary>Short single-flight cache for the admin projection, preventing polling storms.</summary>
public sealed class OutboxOperationsSnapshotCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SingleFlightCache<OutboxOperationsSnapshot> _cache;

    public OutboxOperationsSnapshotCache(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _cache = new SingleFlightCache<OutboxOperationsSnapshot>(Lifetime, timeProvider);
    }

    public Task<OutboxOperationsSnapshot> GetAsync(CancellationToken cancellationToken)
        => _cache.GetAsync(async _ =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<OutboxOperationsSnapshotReader>();
            return await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
        }, cancellationToken);
}
