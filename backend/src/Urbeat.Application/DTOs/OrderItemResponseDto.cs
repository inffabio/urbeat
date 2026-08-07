namespace Urbeat.Application.DTOs;

public sealed class OrderItemResponseDto
{
    public string ProductName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal TotalPrice { get; init; }

    public string? Notes { get; init; }

    public string? VariationName { get; init; }

    public int? WeightGrams { get; init; }

    public string? ChoiceOptionName { get; init; }

    public string? AdditionalNames { get; init; }
}
