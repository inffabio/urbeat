namespace Urbeat.Application.DTOs;

public sealed class CheckoutItemRequestDto
{
    /// <summary>Produto selecionado. O preço é sempre recomputado no backend a partir dele.</summary>
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }

    public string? Notes { get; init; }

    public Guid? VariationId { get; init; }

    public Guid? ChoiceOptionId { get; init; }

    public int? WeightGrams { get; init; }

    public IReadOnlyCollection<Guid>? AdditionalIds { get; init; }

    public IReadOnlyCollection<CheckoutOptionGroupSelectionDto>? OptionGroups { get; init; }
}

public sealed class CheckoutOptionGroupSelectionDto
{
    public Guid GroupId { get; init; }

    public IReadOnlyCollection<Guid> ItemIds { get; init; } = Array.Empty<Guid>();
}
