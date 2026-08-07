namespace Urbeat.Application.DTOs;

public sealed class StoreReviewResponseDto
{
    public Guid Id { get; init; }

    public Guid CustomerUserId { get; init; }

    public int Rating { get; init; }

    public string? Comment { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
