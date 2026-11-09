using Urbeat.Infrastructure.Caching;
using Urbeat.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace Urbeat.WebApi.Health;

/// <summary>
/// Caches the database-derived outbox counters for <see cref="OutboxHealthCheck"/>.
/// <para>
/// The worker heartbeat is intentionally not cached: it is a cheap in-memory read and must be
/// evaluated on every probe so a stalled worker is still reported immediately. Only the aggregate
/// database counts are reused for <see cref="OutboxOptions.HealthCheckCacheDuration"/>; once that
/// window expires the next probe reloads them from PostgreSQL and a failure propagates instead of
/// being hidden by stale data.
/// </para>
/// </summary>
public sealed class OutboxHealthCountsCache
{
    private readonly SingleFlightCache<OutboxHealthCounts> _cache;

    public OutboxHealthCountsCache(IOptions<OutboxOptions> options, TimeProvider timeProvider)
        : this(options.Value.HealthCheckCacheDuration, timeProvider)
    {
    }

    public OutboxHealthCountsCache(TimeSpan lifetime, TimeProvider timeProvider)
    {
        _cache = new SingleFlightCache<OutboxHealthCounts>(lifetime, timeProvider);
    }

    public Task<OutboxHealthCounts> GetAsync(
        Func<CancellationToken, Task<OutboxHealthCounts>> factory,
        CancellationToken cancellationToken)
        => _cache.GetAsync(factory, cancellationToken);
}
