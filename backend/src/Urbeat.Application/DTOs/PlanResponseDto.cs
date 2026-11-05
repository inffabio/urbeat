namespace Urbeat.Application.DTOs;

public sealed class PlanResponseDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Description { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int TrialDays { get; init; }

    public IReadOnlyList<string> Benefits { get; init; } = Array.Empty<string>();
}
