using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Identity;

public sealed class EmailChangeChallengeOptions
{
    public const string SectionName = "EmailChangeChallenge";

    /// <summary>
    /// HMAC signing key. When empty the JWT signing secret is used so the feature works without
    /// additional configuration; production deployments should set a dedicated value.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Lifetime, in minutes, of an issued challenge.</summary>
    public int TtlMinutes { get; set; } = 30;
}

/// <summary>
/// Issues and validates short-lived, signed e-mail change challenges. The challenge carries the
/// user id, the e-mail and the security stamp at issuance, plus an expiry; it is signed with
/// HMAC-SHA256 and never stored. It becomes unusable once the user's security stamp rotates (which
/// happens after a successful e-mail change), giving it single-use semantics without persisting any
/// secret material.
/// </summary>
public sealed class EmailChangeChallengeService : IEmailChangeChallengeService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly byte[] _signingKey;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _timeProvider;

    public EmailChangeChallengeService(
        IOptions<EmailChangeChallengeOptions> options,
        IOptions<JwtOptions> jwtOptions,
        TimeProvider timeProvider)
    {
        var signingKey = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            signingKey = jwtOptions.Value.Secret;
        }

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Email change challenge signing key is not configured.");
        }

        _signingKey = Encoding.UTF8.GetBytes(signingKey);
        _ttl = TimeSpan.FromMinutes(options.Value.TtlMinutes > 0 ? options.Value.TtlMinutes : 30);
        _timeProvider = timeProvider;
    }

    public string CreateChallenge(Guid userId, string email, string securityStamp)
    {
        var payload = new ChallengePayload
        {
            UserId = userId,
            Email = email.Trim().ToLowerInvariant(),
            SecurityStamp = securityStamp,
            ExpiresAtUnix = _timeProvider.GetUtcNow().Add(_ttl).ToUnixTimeSeconds(),
            Nonce = Guid.NewGuid().ToString("N")
        };

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        var signature = HMACSHA256.HashData(_signingKey, payloadBytes);

        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    public EmailChangeChallengeValidation ValidateChallenge(string challenge)
    {
        if (string.IsNullOrWhiteSpace(challenge))
        {
            return new EmailChangeChallengeValidation { Valid = false };
        }

        var parts = challenge.Split('.');
        if (parts.Length != 2)
        {
            return new EmailChangeChallengeValidation { Valid = false };
        }

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            providedSignature = Base64UrlDecode(parts[1]);
        }
        catch
        {
            return new EmailChangeChallengeValidation { Valid = false };
        }

        var expectedSignature = HMACSHA256.HashData(_signingKey, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            return new EmailChangeChallengeValidation { Valid = false };
        }

        ChallengePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ChallengePayload>(payloadBytes, SerializerOptions);
        }
        catch
        {
            return new EmailChangeChallengeValidation { Valid = false };
        }

        if (payload is null
            || payload.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(payload.Email)
            || string.IsNullOrWhiteSpace(payload.SecurityStamp)
            || payload.ExpiresAtUnix < _timeProvider.GetUtcNow().ToUnixTimeSeconds())
        {
            return new EmailChangeChallengeValidation { Valid = false };
        }

        return new EmailChangeChallengeValidation
        {
            Valid = true,
            UserId = payload.UserId,
            Email = payload.Email,
            SecurityStamp = payload.SecurityStamp
        };
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            _ => normalized
        };

        return Convert.FromBase64String(normalized);
    }

    private sealed class ChallengePayload
    {
        public Guid UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        public string SecurityStamp { get; set; } = string.Empty;

        public long ExpiresAtUnix { get; set; }

        public string Nonce { get; set; } = string.Empty;
    }
}
