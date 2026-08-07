namespace Urbeat.Application.DTOs;

public sealed class StoreBusinessHourItemDto
{
    public DayOfWeek DayOfWeek { get; init; }

    public bool IsOpen { get; init; } = true;

    public IReadOnlyCollection<StoreBusinessHourShiftDto> Shifts { get; init; } = [];
}
