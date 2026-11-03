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

    /// <summary>The requested method is not accepted for new orders (e.g. online Pix).</summary>
    public bool PaymentMethodNotAllowed { get; init; }

    /// <summary>
    /// The total shown in the preview differs from the authoritative total. Nothing was persisted;
    /// the client must refresh the preview and ask the customer to confirm the new total.
    /// </summary>
    public bool TotalChanged { get; init; }

    public decimal? AuthoritativeTotal { get; init; }

    /// <summary>The cash change selection is missing, inconsistent or below the authoritative total.</summary>
    public bool InvalidCashChange { get; init; }

    public string? PaymentError { get; init; }

    public CheckoutSummaryResponseDto? Summary { get; init; }

    public CheckoutConfirmResponseDto? Confirmation { get; init; }
}
