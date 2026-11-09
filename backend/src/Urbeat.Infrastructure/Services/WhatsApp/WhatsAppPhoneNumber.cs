using System.Text.RegularExpressions;

namespace Urbeat.Infrastructure.Services.WhatsApp;

internal static partial class WhatsAppPhoneNumber
{
    [GeneratedRegex(@"^\+?[1-9]\d{6,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164Regex();

    /// <summary>
    /// Validates an E.164 number and returns the digits-only form expected by the Cloud API.
    /// </summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (!E164Regex().IsMatch(candidate))
        {
            return false;
        }

        normalized = candidate.StartsWith('+') ? candidate[1..] : candidate;
        return true;
    }
}
