namespace Urbeat.Application.DTOs;

/// <summary>One page of the administrative plan catalog.</summary>
public sealed class PagedPlanResponseDto
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalItems { get; init; }

    public int TotalPages { get; init; }

    public IReadOnlyList<PlanResponseDto> Items { get; init; } = [];
}
