namespace Urbeat.Domain.Entities;

public abstract class BaseEntity
{
    protected BaseEntity()
    {
        Id = Guid.CreateVersion7();
        CreatedAtUtc = DateTime.UtcNow;
    }

    protected BaseEntity(Guid id)
    {
        Id = id;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; protected set; }

    public DateTime CreatedAtUtc { get; protected set; }

    public DateTime? UpdatedAtUtc { get; protected set; }

    public void MarkAsUpdated()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
