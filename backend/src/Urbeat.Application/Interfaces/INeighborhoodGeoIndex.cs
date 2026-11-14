namespace Urbeat.Application.Interfaces;

/// <summary>
/// A match returned by the derived geospatial index. The distance is the straight-line Haversine
/// distance in kilometres; the index never applies a route/circuity factor.
/// </summary>
public sealed record GeoNeighborhoodMatch(Guid NeighborhoodId, double DistanceKm);

/// <summary>
/// Result of reconciling the derived index with the authoritative PostgreSQL rows.
/// </summary>
public sealed record GeoIndexReconciliation(int RedisMembers, int DatabaseMembers, bool Matches);

/// <summary>
/// Derived geospatial index (Redis <c>GEOADD</c>/<c>GEOSEARCH</c>) built exclusively from the
/// active PostgreSQL dataset. It is optional by design: every consumer must keep working when the
/// index is unavailable, falling back to the authoritative Haversine query in PostgreSQL.
/// </summary>
public interface INeighborhoodGeoIndex
{
    /// <summary>False when Redis is not configured/reachable; callers then use the database path.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Rebuilds the version namespace from PostgreSQL. A partial (single-UF) load writes only the
    /// per-UF key; the global key is only built from the full national dataset.
    /// </summary>
    Task<int> RebuildAsync(string datasetVersion, string? uf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches one version namespace. Returns <c>null</c> when the index is unavailable or the
    /// namespace was never built, which is the explicit signal to fall back to PostgreSQL.
    /// </summary>
    Task<IReadOnlyList<GeoNeighborhoodMatch>?> SearchAsync(
        string datasetVersion,
        double latitude,
        double longitude,
        double radiusKm,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<GeoIndexReconciliation?> ReconcileAsync(
        string datasetVersion,
        string? uf,
        CancellationToken cancellationToken = default);
}
