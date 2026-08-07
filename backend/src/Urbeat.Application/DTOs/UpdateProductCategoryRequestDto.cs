namespace Urbeat.Application.DTOs;

public sealed class UpdateProductCategoryRequestDto
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public bool IsFeatured { get; init; }
}
