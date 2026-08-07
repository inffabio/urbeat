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

    public IEnumerable<StoreDeliveryAreaDto> DeliveryAreas { get; init; } = Array.Empty<StoreDeliveryAreaDto>();

    public double AverageRating { get; init; }

    public int TotalReviews { get; init; }
}

