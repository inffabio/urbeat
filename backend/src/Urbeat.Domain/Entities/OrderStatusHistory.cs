namespace Urbeat.Domain.Entities;

public sealed class OrderStatusHistory : BaseEntity
{
    public Guid OrderId { get; set; }

    public OrderStatus PreviousStatus { get; set; }

    public OrderStatus NewStatus { get; set; }

    public Guid ChangedByUserId { get; set; }

    public string? Notes { get; set; }
}
