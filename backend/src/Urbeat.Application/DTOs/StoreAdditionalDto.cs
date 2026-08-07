namespace Urbeat.Application.DTOs;

public sealed class StoreAdditionalGroupDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public sealed class StoreAdditionalDto
{
    public Guid Id { get; init; }
    public Guid StoreId { get; init; }
    public Guid GroupId { get; init; }
    public string GroupName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }
    public int ProductCount { get; init; }
}
