namespace Urbeat.Domain.Entities;

public sealed class Notification : BaseEntity
{
    public Guid RecipientUserId { get; set; }

    public Guid OrderId { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public DateTime? ReadAtUtc { get; set; }
}
