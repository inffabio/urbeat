using Urbeat.Application.DTOs;
using MediatR;

namespace Urbeat.Application.Payments;

public sealed record ProcessMercadoPagoWebhookCommand(string RawPayload, string? IpAddress)
    : IRequest<ProcessWebhookResultDto>;
