using Urbeat.Application.DTOs;
using MediatR;

namespace Urbeat.Application.Subscriptions;

public sealed record ProcessAsaasWebhookCommand(string RawPayload, string? IpAddress)
    : IRequest<ProcessWebhookResultDto>;