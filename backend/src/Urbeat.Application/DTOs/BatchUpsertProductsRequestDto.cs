namespace Urbeat.Application.DTOs;

public sealed class BatchUpsertProductsRequestDto
{
    public IReadOnlyCollection<BatchProductItemDto> Items { get; init; } = Array.Empty<BatchProductItemDto>();
}

public sealed class BatchProductItemDto
{
    public Guid? Id { get; init; }
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsAvailable { get; init; } = true;
    public int DisplayOrder { get; init; }
}
