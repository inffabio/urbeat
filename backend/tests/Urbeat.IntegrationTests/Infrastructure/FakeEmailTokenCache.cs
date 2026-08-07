using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Urbeat.Application.Interfaces;

namespace Urbeat.IntegrationTests.Infrastructure;

public class FakeEmailTokenCache : IEmailTokenCache
{
    private readonly ConcurrentDictionary<string, EmailTokenData> _cache = new();

    public Task SetMappingAsync(string shortCode, Guid userId, string encodedToken, CancellationToken cancellationToken = default)
    {
        _cache[shortCode] = new EmailTokenData { UserId = userId, Token = encodedToken };
        return Task.CompletedTask;
    }

    public Task<EmailTokenData?> GetMappingAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(shortCode, out var val))
        {
            return Task.FromResult<EmailTokenData?>(val);
        }
        return Task.FromResult<EmailTokenData?>(null);
    }
}
