namespace Urbeat.Application.DTOs;

public sealed class DeleteCuisineTypeResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool Protected { get; init; }

    public bool InUse { get; init; }

    public bool Deleted { get; init; }
}
