namespace Urbeat.Domain.Entities;

public sealed class ProductVariation : BaseEntity
{
    public Guid ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Descrição curta da variação (ex.: "30 cm").</summary>
    public string? Description { get; set; }

    /// <summary>Peso em gramas (apenas para variações de peso fixo).</summary>
    public int? WeightGrams { get; set; }

    public decimal Price { get; set; }

    public decimal? PromotionalPrice { get; set; }

    /// <summary>Variação pré-selecionada no cardápio digital (apenas uma por produto).</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }
}
