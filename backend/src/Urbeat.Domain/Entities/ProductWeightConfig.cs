namespace Urbeat.Domain.Entities;

/// <summary>
/// Configuração de venda por peso variável: preço por kg e limites do peso
/// que o cliente pode escolher no cardápio digital.
/// </summary>
public sealed class ProductWeightConfig : BaseEntity
{
    public Guid ProductId { get; set; }

    public decimal PricePerKg { get; set; }

    public int MinGrams { get; set; }

    public int MaxGrams { get; set; }

    public int IncrementGrams { get; set; }

    /// <summary>Indica no cardápio que o preço exibido é estimado.</summary>
    public bool IsEstimated { get; set; }
}
