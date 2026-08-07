namespace Urbeat.Application.DTOs;

public sealed class CreateReviewRequestDto
{
    public int Rating { get; init; }

    public string? Comment { get; init; }
}
