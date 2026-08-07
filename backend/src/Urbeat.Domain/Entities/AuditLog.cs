namespace Urbeat.Domain.Entities;

public sealed class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }

    public string Event { get; set; } = string.Empty;

    public string Entity { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? IpAddress { get; set; }
}
