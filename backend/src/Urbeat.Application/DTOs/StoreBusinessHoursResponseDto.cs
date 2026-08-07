namespace Urbeat.Application.DTOs;

public sealed class StoreBusinessHoursResponseDto
{
    public Guid StoreId { get; init; }

    public IReadOnlyCollection<StoreBusinessHourItemDto> Items { get; init; } = [];
}