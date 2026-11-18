namespace Urbeat.Application.DTOs;

/// <summary>
/// Oferta pública consumida pela landing page. Não expõe timestamps, status
/// interno nem identificadores/parâmetros de gateway.
/// </summary>
public sealed class PublicPlanOfferResponseDto
{
    public IReadOnlyList<PublicPlanOfferItemDto> Plans { get; init; } = Array.Empty<PublicPlanOfferItemDto>();
}

public sealed class PublicPlanOfferItemDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Campo legado (preço mensal). Espelha <see cref="MonthlyAmount"/>.</summary>
    public decimal Amount { get; init; }

    public decimal MonthlyAmount { get; init; }

    public decimal AnnualAmount { get; init; }

    public int AnnualDiscountPercent { get; init; }

    public bool MonthlyEnabled { get; init; }

    public bool AnnualEnabled { get; init; }

    public string Description { get; init; } = string.Empty;

    public int TrialDays { get; init; }

    public IReadOnlyList<string> Benefits { get; init; } = Array.Empty<string>();

    public int Level { get; init; }
}
