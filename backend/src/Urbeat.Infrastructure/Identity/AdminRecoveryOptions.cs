namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Break-glass configuration for recovering the seeded administrator password. It is disabled by
/// default and has no key default: both values must be supplied explicitly by the deployment
/// environment for the endpoint to become available. The key is never logged or returned.
/// </summary>
public sealed class AdminRecoveryOptions
{
    public const string SectionName = "AdminRecovery";

    /// <summary>Minimum accepted length for the recovery key (256 bits at 8 bits per character).</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>Upper bound that keeps the header comparison and hashing bounded.</summary>
    public const int MaximumKeyLength = 512;

    /// <summary>
    /// Feature switch. Defaults to <c>false</c>: the endpoint answers as if it did not exist unless
    /// an operator explicitly enables it for a single recovery operation.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// External recovery key read from the environment/secret store. Intentionally empty by
    /// default; there is no functional fallback and no value is committed to appsettings.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// True only when the feature is enabled and a key with an accepted length is configured.
    /// A configured value with the wrong length is treated as not configured.
    /// </summary>
    public bool IsAvailable => Enabled && IsKeyConfigured;

    public bool IsKeyConfigured =>
        !string.IsNullOrWhiteSpace(Key)
        && Key.Length is >= MinimumKeyLength and <= MaximumKeyLength;
}
