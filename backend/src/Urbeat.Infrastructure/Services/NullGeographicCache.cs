using Urbeat.Application.Interfaces;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Cache used when Redis is not configured. Every read misses and every write is ignored, so the
/// lookup always uses the authoritative PostgreSQL path.
/// </summary>
public sealed class NullGeographicCache : IGeographicCache
{
    public bool IsAvailable => false;

    public Task<string?> GetAsync(string? datasetVersion, string cep, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(
        string? datasetVersion,
        string cep,
        string payload,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
