namespace Urbeat.Application.DTOs;

/// <summary>
/// Oferta pública consumida pela landing page. Não expõe timestamps, status
/// interno nem identificadores de gateway.
/// </summary>
public sealed class PublicPlanOfferResponseDto
{
    public IReadOnlyList<PublicPlanOfferItemDto> Plans { get; init; } = Array.Empty<PublicPlanOfferItemDto>();
}

public sealed class PublicPlanOfferItemDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Description { get; init; } = string.Empty;

    public int TrialDays { get; init; }

    public IReadOnlyList<string> Benefits { get; init; } = Array.Empty<string>();
}
