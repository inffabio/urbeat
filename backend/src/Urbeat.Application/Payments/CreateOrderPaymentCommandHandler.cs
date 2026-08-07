using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using MediatR;

namespace Urbeat.Application.Payments;

public sealed class CreateOrderPaymentCommandHandler : IRequestHandler<CreateOrderPaymentCommand, CreateOrderPaymentResultDto>
{
    private readonly IPaymentService _paymentService;

    public CreateOrderPaymentCommandHandler(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public Task<CreateOrderPaymentResultDto> Handle(CreateOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        return _paymentService.CreateOrderPaymentAsync(
            request.CustomerUserId,
            request.Request,
            request.IpAddress,
            cancellationToken);
    }
}
