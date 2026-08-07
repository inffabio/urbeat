namespace Urbeat.Domain.Entities;

public sealed class Product : BaseEntity
{
    public Guid StoreId { get; set; }

    public Guid CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal? PromotionalPrice { get; set; }

    public string? ImageUrl { get; set; }

    public bool IsAvailable { get; set; } = true;

    public bool IsFeatured { get; set; }

    public int DisplayOrder { get; set; }

    public bool StockEnabled { get; set; }

    public int StockQuantity { get; set; }

    public bool IsBestSeller { get; set; }

    public bool IsNew { get; set; }

    public string TagPriority { get; set; } = string.Empty;

    /// <summary>Forma de venda: "single" | "size" | "fixed_weight" | "variable_weight".</summary>
    public string SaleMode { get; set; } = "single";

    /// <summary>Configuração de venda por peso variável (apenas quando SaleMode = "variable_weight").</summary>
    public ProductWeightConfig? WeightConfig { get; set; }

    public ICollection<ProductAdditional> Additionals { get; set; } = new List<ProductAdditional>();
    public ICollection<ProductAdditionalAssignment> AdditionalAssignments { get; set; } = new List<ProductAdditionalAssignment>();
    public ICollection<ProductChoiceOption> ChoiceOptions { get; set; } = new List<ProductChoiceOption>();
    public ICollection<ProductVariation> Variations { get; set; } = new List<ProductVariation>();
    public ICollection<ProductOptionGroup> OptionGroups { get; set; } = new List<ProductOptionGroup>();
}
