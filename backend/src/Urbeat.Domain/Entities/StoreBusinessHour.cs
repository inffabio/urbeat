namespace Urbeat.Domain.Entities;

public sealed class StoreBusinessHour : BaseEntity
{
    public Guid StoreId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public bool IsOpen { get; set; } = true;

    public ICollection<StoreBusinessHourShift> Shifts { get; set; } = new List<StoreBusinessHourShift>();
}
