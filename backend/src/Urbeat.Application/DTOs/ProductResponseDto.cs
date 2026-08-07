namespace Urbeat.Application.DTOs;

public sealed class ProductResponseDto
{
    public Guid Id { get; init; }
    public Guid StoreId { get; init; }
    public Guid CategoryId { get; init; }
    public string CategoryName { get; set; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? PromotionalPrice { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsFeatured { get; init; }
    public int DisplayOrder { get; init; }
    public bool StockEnabled { get; init; }
    public int StockQuantity { get; init; }
    public bool IsBestSeller { get; init; }
    public bool IsNew { get; init; }
    public string TagPriority { get; init; } = string.Empty;
    public string SaleMode { get; init; } = "single";
    public DateTime CreatedAtUtc { get; init; }

    public IReadOnlyCollection<ProductAdditionalDto> Additionals { get; init; } = Array.Empty<ProductAdditionalDto>();
    public IReadOnlyCollection<ProductChoiceOptionDto> ChoiceOptions { get; init; } = Array.Empty<ProductChoiceOptionDto>();
    public IReadOnlyCollection<ProductVariationDto> Variations { get; init; } = Array.Empty<ProductVariationDto>();
    public IReadOnlyCollection<ProductOptionGroupDto> OptionGroups { get; init; } = Array.Empty<ProductOptionGroupDto>();
    public ProductWeightConfigDto? WeightConfig { get; init; }
}

public sealed class ProductWeightConfigDto
{
    public Guid Id { get; init; }
    public decimal PricePerKg { get; init; }
    public int MinGrams { get; init; }
    public int MaxGrams { get; init; }
    public int IncrementGrams { get; init; }
    public bool IsEstimated { get; init; }
}

public sealed class ProductAdditionalDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public bool IsActive { get; init; }
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed class ProductChoiceOptionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public bool IsActive { get; init; }
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed class ProductVariationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? WeightGrams { get; init; }
    public decimal Price { get; init; }
    public decimal? PromotionalPrice { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
}
