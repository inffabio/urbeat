namespace Urbeat.Application.DTOs;

public sealed class StorePublicDetailsDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty; // Canonical identifier for routing (kebab-case)

    public string PhoneNumber { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CuisineType { get; set; } = string.Empty;

    public string? BannerUrl { get; set; }

    public string? LogoUrl { get; set; }

    public bool IsOpen { get; set; }

    public bool IsOpenNow { get; set; }

    public DateTimeOffset? NextOpeningAt { get; set; }

    public DateTimeOffset? NextStatusChangeAt { get; set; }

    public string? ClosedMessage { get; set; }

    public bool SupportsDelivery { get; set; }

    public bool SupportsPickup { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal MinimumOrderValue { get; set; }

    public decimal? FreeShippingThreshold { get; set; }

    public bool FreeShippingToday { get; set; }

    public int? InitialMinute { get; set; }

    public int? FinalMinute { get; set; }

    public StorePublicAddressDto? Address { get; set; }

    public IReadOnlyCollection<StoreBusinessHourItemDto> BusinessHours { get; set; } = [];

    public double AverageRating { get; set; }

    public int TotalReviews { get; set; }
}
