namespace Urbeat.Application.DTOs;

public sealed class ReviewResponseDto
{
    public Guid Id { get; init; }

    public Guid OrderId { get; init; }

    public int Rating { get; init; }

    public string? Comment { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
