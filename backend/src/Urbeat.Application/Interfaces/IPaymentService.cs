using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IPaymentService
{
    Task<CreateOrderPaymentResultDto> CreateOrderPaymentAsync(
        Guid customerUserId,
        CreateOrderPaymentRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<OrderPaymentResponseDto?> GetOrderPaymentAsync(
        Guid customerUserId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PaymentStatusHistoryResponseDto>> ListOrderPaymentHistoryAsync(
        Guid customerUserId,
        Guid orderId,
        CancellationToken cancellationToken = default);
}
