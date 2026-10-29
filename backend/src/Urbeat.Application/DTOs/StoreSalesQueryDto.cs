namespace Urbeat.Application.DTOs;

public sealed class StoreSalesQueryDto : StoreAnalyticsQueryDto
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
