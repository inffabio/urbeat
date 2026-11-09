using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using MediatR;

namespace Urbeat.Application.WhatsApp;

public sealed class ProcessWhatsAppWebhookCommandHandler
    : IRequestHandler<ProcessWhatsAppWebhookCommand, ProcessWhatsAppWebhookResultDto>
{
    private readonly IWhatsAppWebhookService _whatsAppWebhookService;

    public ProcessWhatsAppWebhookCommandHandler(IWhatsAppWebhookService whatsAppWebhookService)
    {
        _whatsAppWebhookService = whatsAppWebhookService;
    }

    public Task<ProcessWhatsAppWebhookResultDto> Handle(
        ProcessWhatsAppWebhookCommand request,
        CancellationToken cancellationToken)
    {
        return _whatsAppWebhookService.ProcessAsync(request.RawPayload, request.IpAddress, cancellationToken);
    }
}
