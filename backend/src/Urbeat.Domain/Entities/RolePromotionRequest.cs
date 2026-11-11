namespace Urbeat.Domain.Entities;

/// <summary>
/// A pending request to attach a second role (Customer/Seller) to an existing identity after the
/// owner proves control of the e-mail. The password of the new role is persisted only as an
/// Identity password hash; the opaque confirmation token is never persisted (only its SHA-256
/// hash), and the e-mail job rebuilds the token from the request id and the server signing key.
/// At most one request per identity/role is pending at a time; it is superseded by a newer one and
/// applied exactly once.
/// </summary>
public sealed class RolePromotionRequest : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>Normalized (trimmed, lowercase) e-mail of the existing identity.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    public UserRoleType TargetRole { get; set; }

    /// <summary>Hash for the new role credential. Never the plain password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Full name supplied during registration, applied to the Seller role on confirmation.</summary>
    public string? FullName { get; set; }

    /// <summary>SHA-256 hex digest of the opaque confirmation token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Short lease taken by a confirmation attempt before the role and credential are written. It is
    /// released (rolled back) on failure and expires so a crashed attempt never blocks the owner
    /// forever. It is not the definitive consumption marker.
    /// </summary>
    public DateTime? ClaimedAtUtc { get; set; }

    /// <summary>
    /// Identifies the confirmation attempt that holds <see cref="ClaimedAtUtc"/>. The request may
    /// only be marked as applied by this attempt while its lease is still valid, so a stale attempt
    /// (or one whose lease was taken over) can never close a challenge it did not finish.
    /// </summary>
    public Guid? ClaimedBy { get; set; }

    /// <summary>Set only after the role and the independent credential were applied.</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    public DateTime? SupersededAtUtc { get; set; }

    /// <summary>
    /// Set by the e-mail job after the message was handed to the provider. The recovery job
    /// re-enqueues every pending request that still has no value here, so losing the original
    /// enqueue (or the cache entry that used to hold the token) never strands the request.
    /// </summary>
    public DateTime? EmailSentAtUtc { get; set; }

    /// <summary>
    /// Short lease taken before the message is handed to the provider. While it is valid another
    /// job (a duplicate enqueue, a retry or the recovery sweep) must not send again. It expires so
    /// a crashed worker never blocks delivery forever.
    /// </summary>
    public DateTime? EmailSendingAtUtc { get; set; }

    /// <summary>
    /// Identifies the lease owner. Only the attempt that holds the current id may record the send
    /// or release the lease, so a late worker cannot clear a newer attempt.
    /// </summary>
    public Guid? EmailSendAttemptId { get; set; }

    public int ConcurrencyVersion { get; set; }
}
