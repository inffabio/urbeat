namespace Urbeat.Application.DTOs;

public sealed class StorePublicListItemDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty; // Canonical identifier for routing (kebab-case)

    public string CuisineType { get; init; } = string.Empty;

    public bool IsOpen { get; init; }

    public string? LogoUrl { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal MinimumOrderValue { get; init; }

    public decimal? FreeShippingThreshold { get; init; }

    public bool FreeShippingToday { get; set; }

    // Internal mapping aid: the local date the daily promotion was enabled on. Never serialized;
    // the API exposes only the effective FreeShippingToday boolean.
    [System.Text.Json.Serialization.JsonIgnore]
    public DateOnly? FreeShippingTodayDate { get; set; }

    public IEnumerable<StoreDeliveryAreaDto> DeliveryAreas { get; init; } = Array.Empty<StoreDeliveryAreaDto>();

    public double AverageRating { get; init; }

    public int TotalReviews { get; init; }
}

