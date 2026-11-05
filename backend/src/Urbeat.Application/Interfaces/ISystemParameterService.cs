using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ISystemParameterService
{
    Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SystemParameterDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists only the keys in <c>BusinessSystemParameterCatalog</c>, for the
    /// platform business settings screen. Technical parameters are never returned.
    /// </summary>
    Task<IReadOnlyList<SystemParameterDto>> GetBusinessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a business parameter by key. Returns <c>null</c> when the key exists
    /// but is not part of the business catalog, so callers answer 404.
    /// </summary>
    Task<SystemParameterDto?> GetBusinessByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<string> GetValueAsync(string key, CancellationToken cancellationToken = default);

    Task<T> GetValueAsync<T>(string key, CancellationToken cancellationToken = default) where T : IParsable<T>;

    Task SetValueAsync(string key, string value, string? type = null, string? group = null, string? description = null, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(string key);

    Task ReloadAllAsync(CancellationToken cancellationToken = default);
}
