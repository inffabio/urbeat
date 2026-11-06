namespace Urbeat.Domain.Entities;

/// <summary>
/// Password, failed-attempt counter and lockout window for a single role of a single user.
/// The e-mail/identity remains unique in AspNetUsers; this table only splits the credential by
/// role so the same person can have independent Customer and Seller passwords.
/// </summary>
public sealed class UserRoleCredential : BaseEntity
{
    public Guid UserId { get; set; }

    public UserRoleType Role { get; set; }

    /// <summary>
    /// Hash produced by ASP.NET Identity's password hasher. Never store plain text.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public int AccessFailedCount { get; set; }

    public DateTime? LockoutEndUtc { get; set; }
}
