using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Services.Payments;

public interface IOrderPaymentStrategy
{
    bool CanHandle(PaymentMethod method);

    Task<OrderPaymentResponseDto> StartAsync(
        Order order,
        Payment? existingPayment,
        CancellationToken cancellationToken = default);
}
