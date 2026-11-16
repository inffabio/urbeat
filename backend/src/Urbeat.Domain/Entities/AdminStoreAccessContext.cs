namespace Urbeat.Domain.Entities;

/// <summary>
/// Short-lived, single-store delegation granted by an authenticated administrator. The raw token is
/// never persisted: only its versioned hash is stored, and the row is resolved from the
/// <c>X-Admin-Store-Context</c> header on every delegated request.
/// </summary>
public sealed class AdminStoreAccessContext : BaseEntity
{
    /// <summary>Actor. Always derived from the authenticated claim, never from the request body.</summary>
    public Guid AdminUserId { get; set; }

    /// <summary>Single store the context is scoped to.</summary>
    public Guid StoreId { get; set; }

    /// <summary>Versioned digest (<c>ITokenHasher.Hash</c>); never the raw token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// <c>Closed</c>, <c>Limit</c>, <c>Logout</c>, <c>Expired</c>, <c>AdminRoleRemoved</c> or
    /// <c>StoreDeleted</c>.
    /// </summary>
    public string? RevokedReason { get; set; }

    /// <summary>Last accepted request; refreshed at most once per touch interval.</summary>
    public DateTime? LastSeenAtUtc { get; set; }

    /// <summary>Optional, sanitized reason informed when the context was opened.</summary>
    public string? Reason { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Diagnostic only; never used for authorization.</summary>
    public string? UserAgent { get; set; }
}
