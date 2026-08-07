using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IOrderReportService
{
    Task<StoreOrdersSimpleReportResponseDto> GetStoreSimpleReportAsync(
        Guid sellerUserId,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default);
}
