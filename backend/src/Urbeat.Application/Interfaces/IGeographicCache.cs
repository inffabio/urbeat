namespace Urbeat.Application.Interfaces;

/// <summary>
/// Derived cache for geographic lookups (CEP). It is never a source of truth: the payload is
/// always reproducible from PostgreSQL, and a failure/miss must fall back to the database.
/// </summary>
public interface IGeographicCache
{
    bool IsAvailable { get; }

    Task<string?> GetAsync(string? datasetVersion, string cep, CancellationToken cancellationToken = default);

    Task SetAsync(
        string? datasetVersion,
        string cep,
        string payload,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);
}
