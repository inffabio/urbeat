using System.Security.Cryptography;
using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Urbeat.Infrastructure.Services;

public sealed class RedisShortIdCache : IShortIdCache, IDisposable
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    public const int CodeLength = 8;

    private static readonly string GuidPrefix = "shortid:guid:";
    private static readonly string CodePrefix = "shortid:code:";

    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public RedisShortIdCache(IOptions<RedisOptions> options)
    {
        _redis = ConnectionMultiplexer.Connect(options.Value.ConnectionString);
        _db = _redis.GetDatabase();
    }

    public async Task<string?> GetCodeAsync(Guid entityId, CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(new RedisKey(GuidPrefix + entityId));
        return val.HasValue ? val.ToString() : null;
    }

    public async Task<Guid?> GetEntityIdAsync(string code, CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(new RedisKey(CodePrefix + code));
        if (!val.HasValue) return null;
        return Guid.Parse(val.ToString());
    }

    public async Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
    {
        return await _db.KeyExistsAsync(new RedisKey(CodePrefix + code));
    }

    public async Task SetMappingAsync(Guid entityId, string code, CancellationToken ct = default)
    {
        var guidKey = new RedisKey(GuidPrefix + entityId);
        var codeKey = new RedisKey(CodePrefix + code);

        var tran = _db.CreateTransaction();
        _ = tran.StringSetAsync(guidKey, code);
        _ = tran.StringSetAsync(codeKey, entityId.ToString());
        await tran.ExecuteAsync(CommandFlags.None);
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

    public void Dispose()
    {
        _redis.Dispose();
    }
}
