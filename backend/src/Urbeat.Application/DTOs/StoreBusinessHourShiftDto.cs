namespace Urbeat.Application.DTOs;

public sealed class StoreBusinessHourShiftDto
{
    public Guid? Id { get; init; }

    // Nullable so a missing JSON field is distinguishable from the valid 00:00 time.
    public TimeOnly? StartTime { get; init; }

    public TimeOnly? EndTime { get; init; }
}
