namespace Urbeat.Application.DTOs;

public sealed class UpsertStoreBusinessHoursRequestDto
{
    public IReadOnlyCollection<StoreBusinessHourItemDto> Items { get; init; } = [];
}