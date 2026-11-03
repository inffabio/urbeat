namespace Urbeat.Application.DTOs;

public sealed class UpdateOrderConfirmationResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidState { get; init; }

    /// <summary>
    /// New-contract delivery orders can only be concluded after the assigned motoboy confirms the
    /// handover/payment and the 4-digit PIN. A customer confirmation must never bypass either step.
    /// </summary>
    public bool PinRequired { get; init; }

    public OrderDetailsResponseDto? Order { get; init; }
}
