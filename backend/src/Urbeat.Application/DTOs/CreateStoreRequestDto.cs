namespace Urbeat.Application.DTOs;

public sealed class CreateStoreRequestDto
{
    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;
    
    // Pode ser o CNPJ (Vendedor PJ) ou CPF associado diretamente aos dados da loja, caso necessário.
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

    public bool SupportsDelivery { get; init; }

    public bool SupportsPickup { get; init; }

    public int? InitialMinute { get; init; }
    public int? FinalMinute { get; init; }

    public double MaxDeliveryRadiusKm { get; init; }
}
