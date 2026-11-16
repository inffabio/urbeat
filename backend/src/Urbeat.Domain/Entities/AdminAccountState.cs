namespace Urbeat.Domain.Entities;

/// <summary>
/// Explicit operational state of an administrator. It exists so an administrative block is never
/// confused with the automatic Identity lockout: absence of a row means "not blocked".
/// </summary>
public sealed class AdminAccountState : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>Source of truth for the administrative block.</summary>
    public bool IsBlocked { get; set; }

    public DateTime? BlockedAtUtc { get; set; }

    public Guid? BlockedByAdminId { get; set; }
}
