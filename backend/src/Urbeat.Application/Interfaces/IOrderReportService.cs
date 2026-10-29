using Urbeat.Application.Analytics;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IOrderReportService
{
    Task<StoreOrdersSimpleReportResponseDto> GetStoreSimpleReportAsync(
        Guid sellerUserId,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default);

    Task<StoreAnalyticsResponseDto> GetStoreAnalyticsAsync(
        Guid sellerUserId,
        AnalyticsRange range,
        CancellationToken cancellationToken = default);

    Task<PagedStoreSalesResponseDto> GetStoreSalesAsync(
        Guid sellerUserId,
        AnalyticsRange range,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
