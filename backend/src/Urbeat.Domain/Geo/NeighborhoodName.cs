using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Urbeat.Domain.Geo;

/// <summary>
/// Canonical text helpers for geographic names. The normalization contract is shared with
/// <c>backend/scripts/import/cnefe_import.py</c> and must stay byte-for-byte compatible:
/// trim, Unicode NFKD, strip combining marks, replace anything that is not alphanumeric with a
/// space, collapse repeated spaces and lowercase. Naming a logradouro never invents a type or a
/// title that the source does not provide.
/// </summary>
public static class NeighborhoodName
{
    public const int DisplayNameMaxLength = 255;
    public const int ZipCodeLength = 8;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (IsAsciiLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSeparator = false;
                builder.Append(character);
                continue;
            }

            // Any non-alphanumeric character (including decomposed accents, punctuation and
            // whitespace) becomes a single separator that is only emitted between tokens.
            pendingSeparator = true;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds the display name from the available parts, omitting empty ones. In the current
    /// CNEFE artifact there is no street type/title, so the display name is the source
    /// <c>logradouro</c> verbatim (trimmed and with collapsed whitespace).
    /// </summary>
    public static string ComposeDisplayName(string? streetType, string? streetTitle, string? streetName)
    {
        var parts = new List<string>(3);
        AddPart(parts, streetType);
        AddPart(parts, streetTitle);
        AddPart(parts, streetName);

        var composed = string.Join(' ', parts);
        return composed.Length <= DisplayNameMaxLength
            ? composed
            : composed[..DisplayNameMaxLength].TrimEnd();
    }

    /// <summary>
    /// CEP is a string of eight digits. Anything else becomes <c>null</c>; leading zeros are never
    /// fabricated and "00000000" is not used as a placeholder.
    /// </summary>
    public static string? NormalizeZipCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != ZipCodeLength)
        {
            return null;
        }

        // "00000000" is the classic placeholder for "no ZIP". It is never a real CEP and must not
        // be stored as if it were.
        return digits.All(character => character == '0') ? null : digits;
    }

    /// <summary>
    /// Deterministic SHA-256 of the natural source tuple of an address point. Used for
    /// idempotency: re-running the same source version never duplicates a point.
    /// </summary>
    public static string ComputeSourceRowHash(
        string uf,
        string municipalityIbgeCode,
        string? neighborhoodName,
        string? streetName,
        string? zipCode,
        double latitude,
        double longitude)
    {
        var payload = string.Join(
            '|',
            uf?.Trim().ToUpperInvariant() ?? string.Empty,
            municipalityIbgeCode?.Trim() ?? string.Empty,
            Normalize(neighborhoodName),
            Normalize(streetName),
            NormalizeZipCode(zipCode) ?? string.Empty,
            FormatCoordinate(latitude),
            FormatCoordinate(longitude));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string FormatCoordinate(double value)
    {
        return RoundCoordinate(value).ToString("F7", CultureInfo.InvariantCulture);
    }

    private static double RoundCoordinate(double value)
    {
        return double.IsFinite(value) ? Math.Round(value, 7, MidpointRounding.AwayFromZero) : value;
    }

    private static void AddPart(ICollection<string> parts, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (normalized.Length > 0)
        {
            parts.Add(normalized);
        }
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return character is >= 'a' and <= 'z' or >= '0' and <= '9';
    }
}
