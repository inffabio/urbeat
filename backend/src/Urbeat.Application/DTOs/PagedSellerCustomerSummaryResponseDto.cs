namespace Urbeat.Application.DTOs;

public sealed class PagedSellerCustomerSummaryResponseDto
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalItems { get; init; }

    public int TotalPages { get; init; }

    public SellerCustomerMetricsResponseDto Metrics { get; init; } = new();

    public IReadOnlyCollection<SellerCustomerSummaryResponseDto> Items { get; init; } = [];
}
