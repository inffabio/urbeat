namespace Urbeat.Domain.Entities;

public sealed class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }

    public string Event { get; set; } = string.Empty;

    public string Entity { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    /// <summary>Real actor for delegated administrative actions; null on legacy rows.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Store owner in whose name a delegated action ran; null on legacy rows.</summary>
    public Guid? SubjectUserId { get; set; }

    /// <summary>Correlation with <see cref="AdminStoreAccessContext"/>.</summary>
    public Guid? AdminStoreContextId { get; set; }
}
