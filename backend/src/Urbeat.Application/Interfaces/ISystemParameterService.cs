using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ISystemParameterService
{
    Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<string> GetValueAsync(string key, CancellationToken cancellationToken = default);

    Task<T> GetValueAsync<T>(string key, CancellationToken cancellationToken = default) where T : IParsable<T>;

    Task SetValueAsync(string key, string value, string? type = null, string? group = null, string? description = null, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(string key);

    Task ReloadAllAsync(CancellationToken cancellationToken = default);
}
