namespace Urbeat.Application.DTOs;

public sealed class UpsertPaymentGatewayConfigResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public PaymentGatewayConfigResponseDto? Config { get; init; }
}
