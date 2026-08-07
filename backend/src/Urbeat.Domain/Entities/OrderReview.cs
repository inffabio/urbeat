namespace Urbeat.Domain.Entities;

public sealed class OrderReview : BaseEntity
{
    public Guid OrderId { get; set; }

    public Guid StoreId { get; set; }

    public Guid CustomerUserId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }
}
