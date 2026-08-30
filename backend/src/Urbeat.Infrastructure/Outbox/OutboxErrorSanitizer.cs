using System.Text.RegularExpressions;

namespace Urbeat.Infrastructure.Outbox;

/// <summary>
/// Sanitizes error text persisted on outbox messages and returned by the operations endpoint so
/// that no e-mail address, phone number, URL (which may carry a token), credential, bearer token, or
/// JSON secret leaks into durable storage. Safe diagnostic fragments (status names, exception types)
/// are preserved.
/// </summary>
public static partial class OutboxErrorSanitizer
{
    public const int MaxLength = 500;

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])\+?\d{9,15}(?![A-Za-z0-9])", RegexOptions.Compiled)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.Compiled)]
    private static partial Regex UrlPattern();

    // JSON secret values: "token":"…", "access_token":"…", etc. Run before CredentialPattern so the
    // quoted key form is handled as a unit and its value is always redacted.
    [GeneratedRegex(@"(?i)""(token|access[_-]?token|refresh[_-]?token|secret|client[_-]?secret|password|passwd|pwd|api[_-]?key|apikey|authorization|cookie)""\s*:\s*""[^""]*""", RegexOptions.Compiled)]
    private static partial Regex JsonSecretPattern();

    // Bearer tokens in Authorization headers and logs. Run before CredentialPattern so the token
    // value is redacted first and cannot leak past the "authorization:" key match.
    [GeneratedRegex(@"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.Compiled)]
    private static partial Regex BearerPattern();

    // key=value and key: value credentials for common password/key names.
    [GeneratedRegex(@"(?i)\b(token|access[_-]?token|refresh[_-]?token|secret|client[_-]?secret|password|passwd|pwd|api[_-]?key|apikey|authorization|cookie)\s*[:=]\s*(""[^""]*""|[^\s,;}&\]]+)", RegexOptions.Compiled)]
    private static partial Regex CredentialPattern();

    public static string Sanitize(string? error)
    {
        return Sanitize(error, category: null);
    }

    public static string Sanitize(string? error, string? category)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', error.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        collapsed = EmailPattern().Replace(collapsed, "[email]");
        collapsed = UrlPattern().Replace(collapsed, "[url]");
        collapsed = PhonePattern().Replace(collapsed, "[phone]");
        collapsed = JsonSecretPattern().Replace(collapsed, "\"$1\":\"[redacted]\"");
        collapsed = BearerPattern().Replace(collapsed, "Bearer [redacted]");
        collapsed = CredentialPattern().Replace(collapsed, "$1=[redacted]");

        if (!string.IsNullOrWhiteSpace(category))
        {
            collapsed = $"{category}: {collapsed}";
        }

        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength];
    }
}
