namespace Urbeat.Domain.Entities;

/// <summary>
/// Ledger of administrative recovery keys already used. The row is the authoritative one-shot
/// marker: a unique fingerprint of the configured key makes a second successful recovery with the
/// same key impossible, including across replicas and concurrent requests. The key itself is never
/// stored.
/// </summary>
public sealed class AdminRecoveryKeyUsage : BaseEntity
{
    /// <summary>SHA-256 hex digest of the configured recovery key.</summary>
    public string KeyFingerprint { get; set; } = string.Empty;

    /// <summary>Administrator account that was recovered.</summary>
    public Guid AdminUserId { get; set; }

    public DateTime UsedAtUtc { get; set; }

    /// <summary>Request IP only; no credential or token is stored here.</summary>
    public string? IpAddress { get; set; }
}
