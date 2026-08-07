namespace Urbeat.Application.DTOs;

public sealed class ProductOptionGroupDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string ChoiceType { get; init; } = "single";
    public int MinChoices { get; init; }
    public int MaxChoices { get; init; } = 1;
    public int DisplayOrder { get; init; }
    public IReadOnlyCollection<ProductOptionItemDto> Items { get; init; } = Array.Empty<ProductOptionItemDto>();
}

public sealed class ProductOptionItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int DisplayOrder { get; init; }
}
