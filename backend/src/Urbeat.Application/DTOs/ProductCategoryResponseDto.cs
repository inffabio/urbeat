namespace Urbeat.Application.DTOs;

public sealed class ProductCategoryResponseDto
{
    public Guid Id { get; init; }
    public Guid StoreId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    public bool IsFeatured { get; init; }
}
