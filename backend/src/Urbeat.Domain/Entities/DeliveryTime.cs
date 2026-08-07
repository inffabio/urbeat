namespace Urbeat.Domain.Entities;

public sealed class DeliveryTime : BaseEntity
{
    public Guid StoreId { get; set; }

    public int MinTimeMinutes { get; set; }
    public int MaxTimeMinutes { get; set; }

    public string FormattedTime => MaxTimeMinutes > 0 ? $"{MinTimeMinutes}-{MaxTimeMinutes} min" : $"{MinTimeMinutes} min";

    public bool IsActive { get; set; } = true;
}