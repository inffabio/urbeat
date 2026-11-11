using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Dedicated signing configuration for role promotion challenges. There is intentionally no
/// fallback to <see cref="JwtOptions.Secret"/> or to the e-mail change key: a promotion token can
/// grant a second role (and its credential) to an existing identity, so reusing or defaulting a key
/// is not acceptable. The application fails closed at startup when this value is missing, is a
/// known placeholder or does not have enough entropy.
/// </summary>
public sealed class RolePromotionChallengeOptions
{
    public const string SectionName = "RolePromotionChallenge";

    /// <summary>Minimum length accepted for the HMAC key.</summary>
    public const int MinimumKeyLength = 32;

    // Rejects default/example values without ever echoing the configured value.
    private static readonly string[] KnownPlaceholderFragments =
    [
        "CHANGE_ME",
        "CHANGEME",
        "PLACEHOLDER",
        "REPLACE_WITH",
        "YOUR_SECRET",
        "YOUR-KEY",
        "MINHA_CHAVE",
        "SEGREDO",
        "DEFAULT_SECRET"
    ];

    /// <summary>
    /// HMAC-SHA256 key used to derive promotion tokens. It must be supplied by the environment or a
    /// secret store; no value is committed to the repository.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Minimum interval, in seconds, between two delivered promotion e-mails for the same identity.
    /// A repeat request for a role that is already pending reuses the existing request and never
    /// sends again; this window additionally bounds requests that target the other role, so knowing
    /// an e-mail address cannot be used to flood the mailbox. Zero disables the window.
    /// </summary>
    public int CreationCooldownSeconds { get; set; } = 120;

    /// <summary>Normalized cooldown; negative values are rejected by <see cref="Validate"/>.</summary>
    public TimeSpan CreationCooldown =>
        TimeSpan.FromSeconds(CreationCooldownSeconds < 0 ? 0 : CreationCooldownSeconds);

    /// <summary>
    /// Validates the configured key. Messages never contain the key itself, so they are safe to log
    /// and to surface as a startup error.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        if (CreationCooldownSeconds < 0)
        {
            failures.Add($"{SectionName}:CreationCooldownSeconds must be zero or greater.");
        }

        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            failures.Add($"{SectionName}:SigningKey is required.");
            return failures;
        }

        if (SigningKey.Length < MinimumKeyLength)
        {
            failures.Add(
                $"{SectionName}:SigningKey must have at least {MinimumKeyLength} characters.");
        }

        if (IsKnownPlaceholder(SigningKey))
        {
            failures.Add($"{SectionName}:SigningKey must not be a placeholder or known default value.");
        }

        if (SigningKey.Distinct().Count() < 8
            || CalculateEntropyBitsPerCharacter(SigningKey) < 3.0)
        {
            failures.Add(
                $"{SectionName}:SigningKey does not have enough entropy; use a random secret (for example, 32+ bytes from a CSPRNG).");
        }

        return failures;
    }

    private static bool IsKnownPlaceholder(string value)
    {
        var trimmed = value.Trim();
        foreach (var fragment in KnownPlaceholderFragments)
        {
            if (trimmed.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Repeated patterns such as "changeme123456..." are rejected by the entropy check as well,
        // but this catches the common "same character repeated" default.
        return trimmed.Distinct().Count() == 1;
    }

    /// <summary>
    /// Shannon entropy over the character distribution. A random base64/hex secret is well above
    /// 3 bits per character; a human-readable or repeated value stays below it.
    /// </summary>
    internal static double CalculateEntropyBitsPerCharacter(string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        var entropy = 0d;
        foreach (var group in value.GroupBy(character => character))
        {
            var probability = (double)group.Count() / value.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }
}

/// <summary>
/// Wires <see cref="RolePromotionChallengeOptions.Validate"/> into the options pipeline with
/// <c>ValidateOnStart</c>, so a deployment without a valid dedicated key fails on startup instead
/// of silently signing promotion links with a fallback secret.
/// </summary>
public sealed class RolePromotionChallengeOptionsValidator
    : IValidateOptions<RolePromotionChallengeOptions>
{
    public ValidateOptionsResult Validate(string? name, RolePromotionChallengeOptions options)
    {
        var failures = options.Validate();
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
