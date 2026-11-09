using Urbeat.Application.DTOs;
using Urbeat.Infrastructure.Caching;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Short-lived, in-process cache for the public plan offer.
/// <para>
/// The landing page is an anonymous read path that can receive the same request thousands of times
/// per minute. Plan rows change only through <c>IPlanService</c> writes, so the cache is explicitly
/// invalidated by <see cref="CachedPlanService"/> on every create/update/status change. The lifetime
/// is intentionally short (30 seconds) as a second line of defense for writes performed outside the
/// API process, and an expired entry is always reloaded instead of being served stale.
/// </para>
/// </summary>
public sealed class PublicPlanOfferCache
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(30);

    private readonly SingleFlightCache<PublicPlanOfferResponseDto> _cache;

    public PublicPlanOfferCache(TimeProvider timeProvider)
        : this(DefaultLifetime, timeProvider)
    {
    }

    public PublicPlanOfferCache(TimeSpan lifetime, TimeProvider timeProvider)
    {
        _cache = new SingleFlightCache<PublicPlanOfferResponseDto>(lifetime, timeProvider);
    }

    public Task<PublicPlanOfferResponseDto> GetOrCreateAsync(
        Func<CancellationToken, Task<PublicPlanOfferResponseDto>> factory,
        CancellationToken cancellationToken)
        => _cache.GetAsync(factory, cancellationToken);

    public void Invalidate() => _cache.Invalidate();
}
