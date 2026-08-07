using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using MediatR;

namespace Urbeat.Application.Payments;

public sealed class GetOrderPaymentQueryHandler : IRequestHandler<GetOrderPaymentQuery, OrderPaymentResponseDto?>
{
    private readonly IPaymentService _paymentService;

    public GetOrderPaymentQueryHandler(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public Task<OrderPaymentResponseDto?> Handle(GetOrderPaymentQuery request, CancellationToken cancellationToken)
    {
        return _paymentService.GetOrderPaymentAsync(request.CustomerUserId, request.OrderId, cancellationToken);
    }
}
