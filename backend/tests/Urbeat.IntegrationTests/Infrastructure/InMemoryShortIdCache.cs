using System.Collections.Concurrent;
using System.Security.Cryptography;
using Urbeat.Application.Interfaces;

namespace Urbeat.IntegrationTests.Infrastructure;

public sealed class InMemoryShortIdCache : IShortIdCache
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int CodeLength = 25;

    private readonly ConcurrentDictionary<Guid, string> _guidToCode = new();
    private readonly ConcurrentDictionary<string, Guid> _codeToGuid = new();

    public Task<string?> GetCodeAsync(Guid entityId, CancellationToken ct = default)
    {
        _guidToCode.TryGetValue(entityId, out var code);
        return Task.FromResult<string?>(code);
    }

    public Task<Guid?> GetEntityIdAsync(string code, CancellationToken ct = default)
    {
        _codeToGuid.TryGetValue(code, out var guid);
        return Task.FromResult<Guid?>(guid);
    }

    public Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
    {
        return Task.FromResult(_codeToGuid.ContainsKey(code));
    }

    public Task SetMappingAsync(Guid entityId, string code, CancellationToken ct = default)
    {
        _guidToCode[entityId] = code;
        _codeToGuid[code] = entityId;
        return Task.CompletedTask;
    }

    public static string GenerateCode()
    {
        return string.Create(CodeLength, (object?)null, (span, _) =>
        {
            Span<byte> bytes = stackalloc byte[CodeLength];
            RandomNumberGenerator.Fill(bytes);
            for (var i = 0; i < CodeLength; i++)
                span[i] = Alphabet[bytes[i] % Alphabet.Length];
        });
    }
}
