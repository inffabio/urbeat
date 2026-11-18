namespace Urbeat.Application.DTOs;

public sealed class PlanResponseDto
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

    public bool IsActive { get; init; }

    public int TrialDays { get; init; }

    public IReadOnlyList<string> Benefits { get; init; } = Array.Empty<string>();

    public int Level { get; init; }
}
