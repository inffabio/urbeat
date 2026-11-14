using Urbeat.Application.Interfaces;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// No-op geo index used when Redis is not configured. Every search returns <c>null</c> so the caller
/// falls back to the authoritative PostgreSQL query, which is the only source of truth.
/// </summary>
public sealed class NullNeighborhoodGeoIndex : INeighborhoodGeoIndex
{
    public bool IsAvailable => false;

    public Task<int> RebuildAsync(string datasetVersion, string? uf, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<IReadOnlyList<GeoNeighborhoodMatch>?> SearchAsync(
        string datasetVersion,
        double latitude,
        double longitude,
        double radiusKm,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<GeoNeighborhoodMatch>?>(null);
    }

    public Task<GeoIndexReconciliation?> ReconcileAsync(
        string datasetVersion,
        string? uf,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<GeoIndexReconciliation?>(null);
    }
}
