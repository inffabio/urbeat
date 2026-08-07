using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using MediatR;

namespace Urbeat.Application.Payments;

public sealed class GetOrderPaymentHistoryQueryHandler : IRequestHandler<GetOrderPaymentHistoryQuery, IReadOnlyCollection<PaymentStatusHistoryResponseDto>>
{
    private readonly IPaymentService _paymentService;

    public GetOrderPaymentHistoryQueryHandler(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public Task<IReadOnlyCollection<PaymentStatusHistoryResponseDto>> Handle(GetOrderPaymentHistoryQuery request, CancellationToken cancellationToken)
    {
        return _paymentService.ListOrderPaymentHistoryAsync(request.CustomerUserId, request.OrderId, cancellationToken);
    }
}
