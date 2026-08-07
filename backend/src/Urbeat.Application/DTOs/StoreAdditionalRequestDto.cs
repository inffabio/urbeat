namespace Urbeat.Application.DTOs;

public sealed class StoreAdditionalRequestDto
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid GroupId { get; init; }
    public decimal Price { get; init; }
    public bool IsActive { get; init; } = true;
    public int DisplayOrder { get; init; }
}

public sealed class UpdateStoreAdditionalStatusRequestDto
{
    public bool IsActive { get; init; }
}
