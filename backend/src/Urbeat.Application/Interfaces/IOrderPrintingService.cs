using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IOrderPrintingService
{
    Task<PendingPrintJobsResponseDto> ListPendingAsync(
        Guid sellerUserId,
        long afterCursor,
        int limit,
        CancellationToken cancellationToken = default);

    Task<RecordPrintJobResultResponseDto> RecordResultAsync(
        Guid sellerUserId,
        Guid orderId,
        RecordPrintJobResultRequestDto request,
        CancellationToken cancellationToken = default);
}
