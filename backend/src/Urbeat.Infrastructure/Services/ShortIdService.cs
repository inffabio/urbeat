using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Services;

public sealed class ShortIdService : IShortIdService
{
    private const int MaxAttempts = 5;

    private readonly IShortIdCache _cache;
    private readonly ILogger<ShortIdService> _logger;

    public ShortIdService(IShortIdCache cache, ILogger<ShortIdService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> EncodeAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        var existing = await _cache.GetCodeAsync(entityId, cancellationToken);
        if (existing is not null)
            return existing;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var code = RedisShortIdCache.GenerateCode();

            var exists = await _cache.ExistsCodeAsync(code, cancellationToken);
            if (!exists)
            {
                await _cache.SetMappingAsync(entityId, code, cancellationToken);
                return code;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique short code after multiple attempts.");
    }

    public async Task<Guid?> DecodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != RedisShortIdCache.CodeLength)
            return null;

        return await _cache.GetEntityIdAsync(code, cancellationToken);
    }
}
