using StackExchange.Redis;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Services;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Redis-backed derived cache. Keys are versioned by dataset so activating a new import version
/// invalidates the whole namespace without scanning or deleting the previous one.
/// </summary>
public sealed class RedisGeographicCache : IGeographicCache
{
    private readonly IDatabase _database;

    public RedisGeographicCache(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public bool IsAvailable => true;

    public async Task<string?> GetAsync(
        string? datasetVersion,
        string cep,
        CancellationToken cancellationToken = default)
    {
        var value = await _database.StringGetAsync(AddressLookupService.BuildCacheKey(datasetVersion, cep));
        return value.HasValue ? value.ToString() : null;
    }

    public Task SetAsync(
        string? datasetVersion,
        string cep,
        string payload,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        return _database.StringSetAsync(AddressLookupService.BuildCacheKey(datasetVersion, cep), payload, timeToLive);
    }
}
