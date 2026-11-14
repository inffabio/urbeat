using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Geo;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Redis implementation of the derived neighborhood index.
///
/// Contract:
/// * Redis is never the source of truth; every member is copied from PostgreSQL.
/// * Keys are versioned (<c>urbeat:geo:{datasetVersion}:neighborhoods[:uf]</c>), so activating a
///   new dataset version is an atomic pointer change and old keys simply expire.
/// * Manual and pending neighborhoods are never indexed because they have no coordinate.
/// * Rebuilds write into their own version namespace; a partial load never publishes an incomplete
///   global index.
/// </summary>
public sealed class RedisNeighborhoodGeoIndex : INeighborhoodGeoIndex
{
    private const string KeyPrefix = "urbeat:geo";
    private const int BatchSize = 1000;

    private readonly ApplicationDbContext _dbContext;
    private readonly IDatabase _database;

    public RedisNeighborhoodGeoIndex(ApplicationDbContext dbContext, IConnectionMultiplexer redis)
    {
        _dbContext = dbContext;
        _database = redis.GetDatabase();
    }

    public bool IsAvailable => true;

    public async Task<int> RebuildAsync(string datasetVersion, string? uf, CancellationToken cancellationToken = default)
    {
        var key = BuildIndexKey(datasetVersion, uf);
        var normalizedUf = NormalizeUf(uf);

        var query = EligibleNeighborhoodsQuery();
        if (normalizedUf is not null)
        {
            query = query.Where(x => x.CityEntity != null && x.CityEntity.Uf == normalizedUf);
        }

        var total = await query.CountAsync(cancellationToken);

        // Replace only this version's key; the active key is never written in place.
        await _database.KeyDeleteAsync(key);

        var processed = 0;
        var entries = new List<GeoEntry>(BatchSize);

        await foreach (var batch in query
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Latitude, x.Longitude })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            entries.Add(new GeoEntry(batch.Longitude!.Value, batch.Latitude!.Value, batch.Id.ToString("N")));
            processed++;

            if (entries.Count >= BatchSize)
            {
                await _database.GeoAddAsync(key, [.. entries]);
                entries.Clear();
            }
        }

        if (entries.Count > 0)
        {
            await _database.GeoAddAsync(key, [.. entries]);
        }

        var stored = await _database.SortedSetLengthAsync(key);

        await _database.HashSetAsync(
            BuildMetaKey(datasetVersion, normalizedUf),
            [
                new HashEntry("count", stored),
                new HashEntry("uf", normalizedUf ?? "ALL"),
                new HashEntry("activatedAtUtc", DateTime.UtcNow.ToString("O")),
                new HashEntry("sourceVersion", datasetVersion)
            ]);

        Serilog.Log.Information(
            "{EventType} | Geo index rebuilt | Version={Version} | Uf={Uf} | Members={Members} | Expected={Expected}",
            "GEO_INDEX_REBUILT",
            datasetVersion,
            normalizedUf ?? "ALL",
            stored,
            total);

        if (stored != total)
        {
            throw new InvalidOperationException(
                $"Geo index reconciliation failed for version {datasetVersion} (UF {normalizedUf ?? "ALL"}): Redis={stored}, PostgreSQL={total}.");
        }

        return (int)stored;
    }

    public async Task<IReadOnlyList<GeoNeighborhoodMatch>?> SearchAsync(
        string datasetVersion,
        double latitude,
        double longitude,
        double radiusKm,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (!GeoDistance.IsValidBrazilCoordinate(latitude, longitude) || radiusKm <= 0 || !double.IsFinite(radiusKm))
        {
            return [];
        }

        var key = BuildIndexKey(datasetVersion, uf: null);
        if (!await _database.KeyExistsAsync(key))
        {
            // The global namespace was not built yet (partial load): the caller must use PostgreSQL.
            return null;
        }

        // GEOSEARCH BYRADIUS over the complete circle, ascending and optionally bounded. No BYBOX,
        // no bearing restriction and no 1.3 factor.
        var results = await _database.GeoSearchAsync(
            key,
            longitude,
            latitude,
            new GeoSearchCircle(radiusKm, GeoUnit.Kilometers),
            limit ?? 0,
            false,
            Order.Ascending,
            GeoRadiusOptions.WithDistance);

        var matches = new List<GeoNeighborhoodMatch>(results.Length);
        foreach (var result in results)
        {
            if (!Guid.TryParse(result.Member.ToString(), out var neighborhoodId))
            {
                continue;
            }

            var distanceKm = result.Distance ?? GeoDistance.HaversineKm(
                latitude,
                longitude,
                result.Position?.Latitude ?? latitude,
                result.Position?.Longitude ?? longitude);

            if (!GeoDistance.IsWithinRadius(distanceKm, radiusKm))
            {
                continue;
            }

            matches.Add(new GeoNeighborhoodMatch(neighborhoodId, distanceKm));
        }

        return matches;
    }

    public async Task<GeoIndexReconciliation?> ReconcileAsync(
        string datasetVersion,
        string? uf,
        CancellationToken cancellationToken = default)
    {
        var key = BuildIndexKey(datasetVersion, uf);
        if (!await _database.KeyExistsAsync(key))
        {
            return null;
        }

        var normalizedUf = NormalizeUf(uf);
        var query = EligibleNeighborhoodsQuery();
        if (normalizedUf is not null)
        {
            query = query.Where(x => x.CityEntity != null && x.CityEntity.Uf == normalizedUf);
        }

        var databaseMembers = await query.CountAsync(cancellationToken);
        var redisMembers = (int)await _database.SortedSetLengthAsync(key);

        return new GeoIndexReconciliation(redisMembers, databaseMembers, redisMembers == databaseMembers);
    }

    private IQueryable<Domain.Entities.DeliveryNeighborhood> EligibleNeighborhoodsQuery()
    {
        return _dbContext.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.IsActive
                && x.Latitude != null
                && x.Longitude != null
                && x.Kind != GeographicProvenance.NeighborhoodKind.Manual
                && x.CoordinateStatus != GeographicProvenance.CoordinateStatus.Pending);
    }

    private static string? NormalizeUf(string? uf)
    {
        if (string.IsNullOrWhiteSpace(uf))
        {
            return null;
        }

        return uf.Trim().ToUpperInvariant();
    }

    public static string BuildIndexKey(string datasetVersion, string? uf)
    {
        var version = NormalizeVersion(datasetVersion);
        return uf is null
            ? $"{KeyPrefix}:{version}:neighborhoods"
            : $"{KeyPrefix}:{version}:neighborhoods:{uf.Trim().ToLowerInvariant()}";
    }

    public static string BuildMetaKey(string datasetVersion, string? uf)
    {
        var version = NormalizeVersion(datasetVersion);
        return uf is null
            ? $"{KeyPrefix}:{version}:meta"
            : $"{KeyPrefix}:{version}:meta:{uf.Trim().ToLowerInvariant()}";
    }

    private static string NormalizeVersion(string datasetVersion)
    {
        return string.IsNullOrWhiteSpace(datasetVersion)
            ? "unversioned"
            : datasetVersion.Trim().ToLowerInvariant();
    }
}
