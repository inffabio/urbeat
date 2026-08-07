using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class UpsertPaymentGatewayConfigRequestDto
{
    public PaymentGateway Gateway { get; init; }

    public string AccessToken { get; init; } = string.Empty;

    public string? NotificationUrl { get; init; }

    public string Environment { get; init; } = "Sandbox";

    public bool IsActive { get; init; } = true;
}
