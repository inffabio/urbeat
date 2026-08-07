namespace Urbeat.Application.DTOs;

public sealed class CreatePlanRequestDto
{
    public string Name { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Description { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}