namespace Urbeat.Application.DTOs;

public sealed class CreateOrderPaymentResultDto
{
    public bool NotFound { get; init; }

    public bool UnsupportedMethod { get; init; }

    public bool InvalidOrderState { get; init; }

    public OrderPaymentResponseDto? Payment { get; init; }
}
