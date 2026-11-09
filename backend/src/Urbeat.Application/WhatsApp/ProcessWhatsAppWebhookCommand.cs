using Urbeat.Application.DTOs;
using MediatR;

namespace Urbeat.Application.WhatsApp;

public sealed record ProcessWhatsAppWebhookCommand(string RawPayload, string? IpAddress)
    : IRequest<ProcessWhatsAppWebhookResultDto>;
