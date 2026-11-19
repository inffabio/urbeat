namespace Urbeat.Application.DTOs;

/// <summary>Query accepted by the paginated administrative plan catalog.</summary>
public sealed class PlanPageQueryDto
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
