namespace Urbeat.Application.Interfaces;

public interface IShortIdCache
{
    Task<string?> GetCodeAsync(Guid entityId, CancellationToken ct = default);
    Task<Guid?> GetEntityIdAsync(string code, CancellationToken ct = default);
    Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default);
    Task SetMappingAsync(Guid entityId, string code, CancellationToken ct = default);
}
