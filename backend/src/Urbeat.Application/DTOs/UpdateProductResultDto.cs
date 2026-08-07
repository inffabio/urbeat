namespace Urbeat.Application.DTOs;

public sealed class UpdateProductResultDto
{
    public bool NotFound { get; init; }
    public bool Forbidden { get; init; }
    public ProductResponseDto? Product { get; init; }
}
