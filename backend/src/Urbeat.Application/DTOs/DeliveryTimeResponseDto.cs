namespace Urbeat.Application.DTOs;

public sealed class DeliveryTimeResponseDto
{
    public Guid Id { get; init; }

    public int MinTimeMinutes { get; init; }

    public int MaxTimeMinutes { get; init; }

    public string FormattedTime { get; init; } = string.Empty;
}
