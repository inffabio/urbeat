using System.Security.Cryptography;
using System.Text.Json;
using Urbeat.Application.Interfaces;
using StackExchange.Redis;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services;

public sealed class RedisEmailTokenCache : IEmailTokenCache, IDisposable
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    public const int CodeLength = 25;
    private static readonly string CodePrefix = "emailtoken:code:";

    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public RedisEmailTokenCache(IOptions<RedisOptions> options)
    {
        _redis = ConnectionMultiplexer.Connect(options.Value.ConnectionString);
        _db = _redis.GetDatabase();
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

    public async Task SetMappingAsync(string code, Guid userId, string token, CancellationToken ct = default)
    {
        var data = new EmailTokenData { UserId = userId, Token = token };
        var json = JsonSerializer.Serialize(data);
        var codeKey = new RedisKey(CodePrefix + code);
        // Expiry of 48 hours for the short code mapping
        await _db.StringSetAsync(codeKey, json, TimeSpan.FromHours(48));
    }

    public async Task<EmailTokenData?> GetMappingAsync(string code, CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(new RedisKey(CodePrefix + code));
        if (!val.HasValue) return null;
        return JsonSerializer.Deserialize<EmailTokenData>(val.ToString());
    }

    public void Dispose()
    {
        _redis.Dispose();
    }
}