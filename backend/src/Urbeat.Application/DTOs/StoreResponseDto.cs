namespace Urbeat.Application.DTOs;

public sealed class StoreResponseDto
{
    public Guid Id { get; init; }

    public Guid OwnerUserId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty; // Canonical identifier for routing (kebab-case)

    public string PhoneNumber { get; init; } = string.Empty;

    public string? Document { get; init; }

    public string? PixKey { get; init; }

    public string? InstagramUrl { get; init; }

    public string? FacebookUrl { get; init; }

    public string? TikTokUrl { get; init; }

    public string? WebsiteUrl { get; init; }

    public string Description { get; init; } = string.Empty;

    public string CuisineType { get; init; } = string.Empty;

    public string? BannerUrl { get; init; }

    public string? LogoUrl { get; init; }

    public bool IsOpen { get; init; }

    public bool IsSubscriptionBlocked { get; init; }

    public bool SupportsDelivery { get; init; }

    public bool SupportsPickup { get; init; }

    public int? InitialMinute { get; init; }
    public int? FinalMinute { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal MinimumOrderValue { get; init; }

    public decimal? FreeShippingThreshold { get; init; }

    public bool FreeShippingToday { get; init; }

    public IEnumerable<StoreDeliveryAreaDto> DeliveryAreas { get; set; } = Array.Empty<StoreDeliveryAreaDto>();

    public double AverageRating { get; init; }

    public int TotalReviews { get; init; }

    public double? MaxDeliveryRadiusKm { get; init; }

    public double? LastImportedRadiusKm { get; init; }
}
