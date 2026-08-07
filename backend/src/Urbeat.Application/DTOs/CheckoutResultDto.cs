namespace Urbeat.Application.DTOs;

public sealed class CheckoutResultDto
{
    public bool StoreNotFound { get; init; }

    public bool AddressNotFound { get; init; }

    public bool StoreClosed { get; init; }

    public bool StoreBlocked { get; init; }

    public bool BelowMinimum { get; init; }

    public bool MinimumNotMetForPickUp { get; set; }

    public bool DeliveryAreaNotCovered { get; init; }

    public bool InvalidItems { get; init; }

    public string? ItemError { get; init; }

    public CheckoutSummaryResponseDto? Summary { get; init; }

    public CheckoutConfirmResponseDto? Confirmation { get; init; }
}
