using Urbeat.Domain.Enums;

namespace Urbeat.Application.DTOs;

public sealed class UpdateProductRequestDto
{
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? PromotionalPrice { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsAvailable { get; init; } = true;
    /// <summary>Classificação especial única e opcional: Offers, BestSeller ou New.</summary>
    public ProductSpecialClassification? SpecialClassification { get; init; }
    public int DisplayOrder { get; init; }
    public bool StockEnabled { get; init; }
    public int StockQuantity { get; init; }
    public string TagPriority { get; init; } = string.Empty;
    public string SaleMode { get; init; } = "single";
    public ProductWeightConfigRequestDto? WeightConfig { get; init; }

    public IReadOnlyCollection<ProductAdditionalDto> Additionals { get; init; } = Array.Empty<ProductAdditionalDto>();
    public IReadOnlyCollection<Guid>? AdditionalIds { get; init; }
    public IReadOnlyCollection<ProductChoiceOptionDto> ChoiceOptions { get; init; } = Array.Empty<ProductChoiceOptionDto>();
    public IReadOnlyCollection<ProductVariationDto> Variations { get; init; } = Array.Empty<ProductVariationDto>();
    public IReadOnlyCollection<ProductOptionGroupDto> OptionGroups { get; init; } = Array.Empty<ProductOptionGroupDto>();
}
