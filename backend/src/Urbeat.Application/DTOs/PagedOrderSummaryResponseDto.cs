namespace Urbeat.Application.DTOs;

public sealed class PagedOrderSummaryResponseDto
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalItems { get; set; }

    public int TotalPages { get; set; }

    public IReadOnlyCollection<OrderSummaryResponseDto> Items { get; set; } = [];
}
