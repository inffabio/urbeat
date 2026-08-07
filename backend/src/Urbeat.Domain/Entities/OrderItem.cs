namespace Urbeat.Domain.Entities;

public sealed class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }

    public string? Notes { get; set; }

    public string? VariationName { get; set; }

    /// <summary>Peso escolhido em gramas (produtos vendidos por peso variável).</summary>
    public int? WeightGrams { get; set; }

    public string? ChoiceOptionName { get; set; }

    public string? AdditionalNames { get; set; }
}
