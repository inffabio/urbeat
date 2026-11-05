using System.Collections.Concurrent;
using System.Globalization;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.SystemParameters;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Services;

public sealed class SystemParameterService : ISystemParameterService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SystemParameterService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public SystemParameterService(IServiceScopeFactory scopeFactory, ILogger<SystemParameterService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entities = await db.SystemParameters
            .AsNoTracking()
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Key)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<SystemParameterDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entity = await db.SystemParameters
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key, cancellationToken);

        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<SystemParameterDto>> GetBusinessAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entities = await db.SystemParameters
            .AsNoTracking()
            .Where(p => BusinessSystemParameterCatalog.Keys.Contains(p.Key))
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Key)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<SystemParameterDto?> GetBusinessByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!BusinessSystemParameterCatalog.Contains(key))
            return null;

        return await GetByKeyAsync(key, cancellationToken);
    }

    public async Task<string> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        await RefreshIfEmptyAsync(cancellationToken);

        return _cache.TryGetValue(key, out cached) ? cached : string.Empty;
    }

    public async Task<T> GetValueAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : IParsable<T>
    {
        var raw = await GetValueAsync(key, cancellationToken);
        return string.IsNullOrWhiteSpace(raw)
            ? throw new KeyNotFoundException($"System parameter '{key}' not found or is empty.")
            : T.Parse(raw, CultureInfo.InvariantCulture);
    }

    public async Task SetValueAsync(string key, string value, string? type = null, string? group = null, string? description = null, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var parameter = await db.SystemParameters
            .FirstOrDefaultAsync(p => p.Key == key, cancellationToken);

        if (parameter is null)
        {
            var parsedType = SystemParameterType.String;
            if (type is not null)
                Enum.TryParse(type, ignoreCase: true, out parsedType);

            parameter = new SystemParameter
            {
                Key = key,
                Value = value,
                Type = parsedType,
                Group = group,
                Description = description,
            };
            db.SystemParameters.Add(parameter);
        }
        else
        {
            parameter.Value = value;
            if (group is not null) parameter.Group = group;
            if (description is not null) parameter.Description = description;
            parameter.MarkAsUpdated();
        }

        await db.SaveChangesAsync(cancellationToken);
        _cache[key] = value;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var parameter = await db.SystemParameters
            .FirstOrDefaultAsync(p => p.Key == key, cancellationToken);

        if (parameter is null)
            return;

        db.SystemParameters.Remove(parameter);
        await db.SaveChangesAsync(cancellationToken);
        _cache.TryRemove(key, out _);
    }

    public Task InvalidateCacheAsync(string key)
    {
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public async Task ReloadAllAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await ReloadAllCoreAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task ReloadAllCoreAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var parameters = await db.SystemParameters
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dict = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parameters)
            dict[p.Key] = p.Value;

        Interlocked.Exchange(ref _cache, dict);

        _logger.LogInformation("SystemParameterService: cache reloaded with {Count} entries", dict.Count);
    }

    private async Task RefreshIfEmptyAsync(CancellationToken cancellationToken)
    {
        if (!_cache.IsEmpty)
            return;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!_cache.IsEmpty)
                return;

            // Reuse the lock-aware core: calling ReloadAllAsync here would try
            // to acquire _refreshLock again and deadlock the first read.
            await ReloadAllCoreAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static SystemParameterDto ToDto(SystemParameter entity)
    {
        var policy = SystemParameterCatalog.Resolve(entity.Key, entity.Type, entity.Group);

        return new SystemParameterDto(
            entity.Id,
            entity.Key,
            policy.Secret ? SystemParameterCatalog.SecretMask : entity.Value,
            entity.Type.ToString(),
            entity.Group,
            entity.Description,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            policy.MaxLength,
            policy.Display.ToWireName(),
            policy.Unit,
            policy.MinValue,
            policy.MaxValue,
            policy.ReadOnly,
            policy.Restricted,
            policy.Secret);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _refreshLock.Dispose();
        _disposed = true;
    }
}
