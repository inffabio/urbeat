using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using MediatR;

namespace Urbeat.Application.Payments;

public sealed class ProcessMercadoPagoWebhookCommandHandler : IRequestHandler<ProcessMercadoPagoWebhookCommand, ProcessWebhookResultDto>
{
    private readonly IPaymentWebhookService _paymentWebhookService;

    public ProcessMercadoPagoWebhookCommandHandler(IPaymentWebhookService paymentWebhookService)
    {
        _paymentWebhookService = paymentWebhookService;
    }

    public Task<ProcessWebhookResultDto> Handle(ProcessMercadoPagoWebhookCommand request, CancellationToken cancellationToken)
    {
        return _paymentWebhookService.ProcessMercadoPagoWebhookAsync(request.RawPayload, request.IpAddress, cancellationToken);
    }
}
