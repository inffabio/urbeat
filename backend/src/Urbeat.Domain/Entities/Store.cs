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

    public string? InstagramUrl { get; set; }

    public string? FacebookUrl { get; set; }

    public string? TikTokUrl { get; set; }

    public string? WebsiteUrl { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid? CuisineTypeId { get; set; }
    public CuisineType? CuisineType { get; set; }

    public string? BannerUrl { get; set; }

    public string? LogoUrl { get; set; }

    public bool IsOpen { get; set; }

    public bool IsSubscriptionBlocked { get; set; }

    public bool SupportsDelivery { get; set; }

    public bool SupportsPickup { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal MinimumOrderValue { get; set; }

    public decimal? FreeShippingThreshold { get; set; }

    public bool FreeShippingToday { get; set; }

    public int? InitialMinute { get; set; }
    public int? FinalMinute { get; set; }

    public double AverageRating { get; set; }

    public int TotalReviews { get; set; }

    public double? MaxDeliveryRadiusKm { get; set; }

    public double? LastImportedRadiusKm { get; set; }

    public ICollection<StoreDeliveryArea> DeliveryAreas { get; set; } = new List<StoreDeliveryArea>();
}
