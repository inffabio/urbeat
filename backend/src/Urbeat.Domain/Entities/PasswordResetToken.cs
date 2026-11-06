namespace Urbeat.Domain.Entities;

public sealed class PasswordResetToken : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Role whose credential this token may change. Legacy tokens created before per-role
    /// credentials have no role and are rejected by the role-aware recovery flow.
    /// </summary>
    public UserRoleType? Role { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool Used { get; set; }
}
