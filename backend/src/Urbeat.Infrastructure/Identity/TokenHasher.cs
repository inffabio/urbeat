using System.Security.Cryptography;
using System.Text;
using Urbeat.Application.Interfaces;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Versioned SHA-256 digest for short-lived opaque tokens. It is deliberately independent from the
/// password hasher: tokens are random and high-entropy, so Argon2id's memory cost would only make
/// confirmation/reset endpoints slower and more expensive to serve. The <c>t1.</c> prefix makes the
/// format unambiguous, and an Argon2id/PBKDF2 password hash can never be accepted here.
/// </summary>
public sealed class TokenHasher : ITokenHasher
{
    private const string Prefix = "t1.";
    private const int DigestLength = 32;

    private static readonly char[] Base64UrlCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".ToCharArray();

    public string Hash(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw new ArgumentException("A token is required to compute its hash.", nameof(rawToken));
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Prefix + Base64UrlEncode(digest);
    }

    public bool Verify(string rawToken, string hash)
    {
        if (string.IsNullOrWhiteSpace(rawToken)
            || string.IsNullOrWhiteSpace(hash)
            || !hash.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encoded = hash[Prefix.Length..];
        if (!TryBase64UrlDecode(encoded, out var providedDigest))
        {
            return false;
        }

        var expectedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return CryptographicOperations.FixedTimeEquals(expectedDigest, providedDigest);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = [];

        // 32 bytes encode to 43 base64url chars.
        if (value.Length != 43)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (Array.IndexOf(Base64UrlCharacters, character) < 0)
            {
                return false;
            }
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += "=";

        try
        {
            bytes = Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            return false;
        }

        return bytes.Length == DigestLength;
    }
}
