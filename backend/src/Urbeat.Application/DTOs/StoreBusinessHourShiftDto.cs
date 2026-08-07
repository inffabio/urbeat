namespace Urbeat.Application.DTOs;

public sealed class StoreBusinessHourShiftDto
{
    public Guid? Id { get; init; }

    public TimeOnly StartTime { get; init; }

    public TimeOnly EndTime { get; init; }
}
