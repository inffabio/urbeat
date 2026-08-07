namespace Urbeat.Application.DTOs;

public sealed class UpsertProductCategoryResultDto
{
    public bool NotFound { get; init; }
    public bool Forbidden { get; init; }
    public bool Conflict { get; init; }
    public ProductCategoryResponseDto? Category { get; init; }
}
