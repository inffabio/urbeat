namespace Urbeat.Application.DTOs;

public sealed class CreateDeliveryTimeRequestDto
{
    public Guid StoreId { get; init; }

    public int MinTimeMinutes { get; init; }

    public int MaxTimeMinutes { get; init; }
}
