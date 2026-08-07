using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class PaymentGatewayConfigResponseDto
{
    public Guid StoreId { get; init; }

    public PaymentGateway Gateway { get; init; }

    public bool HasAccessToken { get; init; }

    public bool HasNotificationUrl { get; init; }

    public string Environment { get; init; } = "Sandbox";

    public bool IsActive { get; init; }
}
