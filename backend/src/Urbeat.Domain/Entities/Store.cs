namespace Urbeat.Domain.Entities;

public sealed class Store : BaseEntity
{
    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    // Canonical identifier for routing (kebab-case, e.g., "pizza-hunter")
    public string Slug { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? Document { get; set; }

    public string? PixKey { get; set; }

    public string? WebsiteUrl { get; set; }

    public Guid? CuisineTypeId { get; set; }
    public CuisineType? CuisineType { get; set; }

    public string? BannerUrl { get; set; }

    public string? LogoUrl { get; set; }

    public bool IsOpen { get; set; }

    // Persisted publication state. Set to true only after a successful publish action;
    // it must never be derived from operational open/closed state or wizard completion.
    public bool IsPublished { get; set; }

    public bool IsSubscriptionBlocked { get; set; }

    public bool SupportsDelivery { get; set; }

    public bool SupportsPickup { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal MinimumOrderValue { get; set; }

    public decimal? FreeShippingThreshold { get; set; }

    public bool FreeShippingToday { get; set; }

    // Local Sao Paulo calendar date on which the daily free shipping promotion was enabled.
    // Null (or a date other than today) means the promotion is not active.
    public DateOnly? FreeShippingTodayDate { get; set; }

    public int? InitialMinute { get; set; }
    public int? FinalMinute { get; set; }

    public double AverageRating { get; set; }

    public int TotalReviews { get; set; }

    public double? MaxDeliveryRadiusKm { get; set; }

    public double? LastImportedRadiusKm { get; set; }

    public ICollection<StoreDeliveryArea> DeliveryAreas { get; set; } = new List<StoreDeliveryArea>();
}
