using System.Security.Cryptography;
using System.Text;

namespace Urbeat.Infrastructure.Services.WhatsApp;

/// <summary>
/// Meta webhook authentication helpers. Both checks are fail-closed and use fixed-time
/// comparisons; no expected or provided secret is ever logged.
/// </summary>
public static class WhatsAppWebhookSignature
{
    private const string SignaturePrefix = "sha256=";

    public static bool IsValidSignature(string appSecret, ReadOnlySpan<byte> payload, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(appSecret)
            || string.IsNullOrEmpty(signatureHeader)
            || !signatureHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var providedHex = signatureHeader[SignaturePrefix.Length..];
        if (providedHex.Length != 64)
        {
            return false;
        }

        byte[] providedSignature;
        try
        {
            providedSignature = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), payload);
        return CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature);
    }

    public static bool IsValidSignature(string appSecret, string payload, string? signatureHeader)
    {
        return IsValidSignature(appSecret, Encoding.UTF8.GetBytes(payload), signatureHeader);
    }

    public static string ComputeSignature(string appSecret, ReadOnlySpan<byte> payload)
    {
        return SignaturePrefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), payload));
    }

    /// <summary>
    /// Compares a webhook verification token in fixed time. Hashing both sides keeps the
    /// comparison independent of the token length.
    /// </summary>
    public static bool TokensMatch(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || provided is null)
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
