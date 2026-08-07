using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using MediatR;

namespace Urbeat.Application.Subscriptions;

public sealed class ProcessAsaasWebhookCommandHandler : IRequestHandler<ProcessAsaasWebhookCommand, ProcessWebhookResultDto>
{
    private readonly ISubscriptionWebhookService _subscriptionWebhookService;

    public ProcessAsaasWebhookCommandHandler(ISubscriptionWebhookService subscriptionWebhookService)
    {
        _subscriptionWebhookService = subscriptionWebhookService;
    }

    public Task<ProcessWebhookResultDto> Handle(ProcessAsaasWebhookCommand request, CancellationToken cancellationToken)
    {
        return _subscriptionWebhookService.ProcessAsaasWebhookAsync(request.RawPayload, request.IpAddress, cancellationToken);
    }
}