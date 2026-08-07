namespace Urbeat.Application.DTOs;

/// <summary>
/// Resultado do cálculo autoritativo de preço de um item, feito no backend.
/// O cliente nunca envia preço; ele é sempre recomputado a partir do produto.
/// </summary>
public sealed class ItemPricingResultDto
{
    public bool IsValid { get; init; } = true;

    public string? Error { get; init; }

    public decimal UnitPrice { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string? VariationName { get; init; }

    public string? ChoiceOptionName { get; init; }

    /// <summary>Peso selecionado (gramas) para produtos vendidos por peso variável.</summary>
    public int? WeightGrams { get; init; }

    /// <summary>Rótulo de peso para snapshot no pedido (ex.: "500 g").</summary>
    public string? WeightLabel { get; init; }

    /// <summary>Nomes dos adicionais e itens de grupos selecionados (snapshot).</summary>
    public IReadOnlyCollection<string> ExtraNames { get; init; } = Array.Empty<string>();

    public static ItemPricingResultDto Invalid(string error) => new() { IsValid = false, Error = error };
}
