namespace Urbeat.Domain.Entities;

public sealed class StoreBusinessHourShift : BaseEntity
{
    public Guid StoreBusinessHourId { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }
}
