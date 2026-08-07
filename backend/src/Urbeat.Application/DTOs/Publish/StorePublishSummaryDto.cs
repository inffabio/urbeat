namespace Urbeat.Application.DTOs.Publish;

public class StorePublishSummaryDto
{
    public StoreDetailsSummaryDto StoreDetails { get; set; } = new();
    public List<StoreBusinessHourItemDto> BusinessHours { get; set; } = new();
    public StoreDeliveryFeesSummaryDto DeliveryFees { get; set; } = new();
    public List<StoreDeliveryAreaSummaryDto> DeliveryAreas { get; set; } = new();
    public StoreProductsStatsDto ProductsStats { get; set; } = new();
    public List<StoreProductPreviewDto> ProductsPreview { get; set; } = new();
    public StorePublishRulesDto Rules { get; set; } = new();
    
    public int CompletionPercentage { get; set; }
    public bool CanPublish { get; set; }
}

public class StoreDetailsSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public string CuisineType { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
}

public class StoreDeliveryFeesSummaryDto
{
    public decimal BaseFee { get; set; }
    public decimal MinimumOrderValue { get; set; }
    public string EstimatedTimeMin { get; set; } = string.Empty;
    public decimal? FreeShippingThreshold { get; set; }
}

public class StoreDeliveryAreaSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public decimal DeliveryFee { get; set; }
}

public class StoreProductsStatsDto
{
    public int Total { get; set; }
    public List<StoreCategoryStatDto> ByCategory { get; set; } = new();
}

public class StoreCategoryStatDto
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class StoreProductPreviewDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class StorePublishRulesDto
{
    public bool DetailsOk { get; set; }
    public bool HoursOk { get; set; }
    public bool DeliveryOk { get; set; }
    public bool ProductsOk { get; set; }
}
