namespace Urbeat.Application.DTOs;

public sealed class ReorderStoreCategoriesRequestDto : List<ReorderStoreCategoriesItemDto>
{
}

public sealed class ReorderStoreCategoriesItemDto
{
    public Guid Id { get; init; }
    public int DisplayOrder { get; init; }
}
