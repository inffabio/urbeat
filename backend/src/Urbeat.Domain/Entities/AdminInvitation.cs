namespace Urbeat.Domain.Entities;

/// <summary>
/// A single-use administrative e-mail link (account invitation or password reset). Only the token
/// hash is persisted; the raw token is derived from <see cref="BaseEntity.Id"/>, the purpose and
/// the server signing key, so the delivery job can rebuild it without storing a secret.
/// </summary>
public sealed class AdminInvitation : BaseEntity
{
    /// <summary>
    /// Target identity. It becomes null only when a pending invitation is cancelled and the
    /// still-unverified identity is deleted; the e-mail snapshot keeps the audit trail meaningful.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>Normalized (trimmed, lowercase) address used as recipient and for display.</summary>
    public string Email { get; set; } = string.Empty;

    public AdminInvitationPurpose Purpose { get; set; }

    /// <summary><c>ITokenHasher.Hash(token)</c>. The raw token is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set only after the password has been applied.</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>Manual cancellation marker.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>A newer link replaced this one.</summary>
    public DateTime? SupersededAtUtc { get; set; }

    /// <summary>Actor that issued the link; null for flows without an authenticated administrator.</summary>
    public Guid? CreatedByAdminId { get; set; }

    /// <summary>Set by the delivery job after the provider accepted the message.</summary>
    public DateTime? EmailSentAtUtc { get; set; }

    /// <summary>Short lease taken by the delivery job before handing the message to the provider.</summary>
    public DateTime? EmailSendingAtUtc { get; set; }

    public Guid? EmailSendAttemptId { get; set; }

    /// <summary>Short lease taken by the consumption attempt before the password is applied.</summary>
    public DateTime? ClaimedAtUtc { get; set; }

    public Guid? ClaimedBy { get; set; }

    public int ConcurrencyVersion { get; set; }
}
