using Urbeat.Application.DTOs;
using Urbeat.Application.DTOs.Publish;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Interfaces.Publish;

namespace Urbeat.Application.Services.Publish;

public class StorePublishService : IStorePublishService
{
    private readonly IStoreService _storeService;
    private readonly IStoreAddressService _storeAddressService;
    private readonly IStoreBusinessHoursService _storeBusinessHoursService;
    private readonly IProductService _productService;

    public StorePublishService(
        IStoreService storeService,
        IStoreAddressService storeAddressService,
        IStoreBusinessHoursService storeBusinessHoursService,
        IProductService productService)
    {
        _storeService = storeService;
        _storeAddressService = storeAddressService;
        _storeBusinessHoursService = storeBusinessHoursService;
        _productService = productService;
    }

    public async Task<StorePublishSummaryDto> GetStorePublishSummaryAsync(Guid storeId, Guid ownerId, CancellationToken cancellationToken)
    {
        var store = await _storeService.GetByOwnerAsync(ownerId, cancellationToken);
        if (store == null || store.Id != storeId)
            throw new Exception("Store not found or access denied.");

        var address = await _storeAddressService.GetByStoreAsync(ownerId, storeId, cancellationToken);
        var hours = await _storeBusinessHoursService.GetAsync(ownerId, storeId, cancellationToken);
        var products = await _productService.ListByStoreAsync(ownerId, storeId, cancellationToken);

        var summary = new StorePublishSummaryDto();

        summary.StoreDetails.Name = store.Name;
        summary.StoreDetails.CuisineType = store.CuisineType;
        summary.StoreDetails.PhoneNumber = store.PhoneNumber;
        summary.StoreDetails.Description = store.Description;
        summary.StoreDetails.LogoUrl = store.LogoUrl;
        summary.StoreDetails.BannerUrl = store.BannerUrl;

        if (address != null)
        {
            summary.StoreDetails.Address = $"{address.Street}, {address.Number}";
            summary.StoreDetails.City = address.City;
        }

        if (hours != null && hours.Items != null)
        {
            summary.BusinessHours = hours.Items
                .Where(h => h.IsOpen && h.Shifts.Count > 0)
                .ToList();
        }

        summary.DeliveryFees.BaseFee = store.DeliveryFee;
        summary.DeliveryFees.MinimumOrderValue = store.MinimumOrderValue;
        summary.DeliveryFees.EstimatedTimeMin = store.InitialMinute.HasValue && store.FinalMinute.HasValue
            ? $"{store.InitialMinute}-{store.FinalMinute} min"
            : string.Empty;
        summary.DeliveryFees.FreeShippingThreshold = store.FreeShippingThreshold;

        if (store.DeliveryAreas != null && store.DeliveryAreas.Any())
        {
            summary.DeliveryAreas = store.DeliveryAreas.Select(a => new StoreDeliveryAreaSummaryDto
            {
                Name = a.Neighborhood,
                DeliveryFee = a.DeliveryFee
            }).ToList();
        }

        if (products != null && products.Any())
        {
            var activeProducts = products.Where(p => p.IsAvailable).ToList();
            var categoryGroups = products.GroupBy(p => p.CategoryName ?? "Sem categoria");

            summary.ProductsStats.Total = products.Count;
            summary.ProductsStats.ByCategory = categoryGroups
                .Select(g => new StoreCategoryStatDto
                {
                    Name = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(c => c.Count)
                .ToList();

            summary.ProductsPreview = activeProducts
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.Name)
                .Select(p => new StoreProductPreviewDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    ImageUrl = p.ImageUrl,
                    CategoryName = p.CategoryName ?? string.Empty,
                    IsActive = p.IsAvailable
                }).ToList();
        }

        summary.Rules.DetailsOk = !string.IsNullOrEmpty(store.Name) &&
                                  !string.IsNullOrEmpty(store.CuisineType) &&
                                  !string.IsNullOrEmpty(store.PhoneNumber) &&
                                  !string.IsNullOrEmpty(store.Description) &&
                                  address != null;

        summary.Rules.HoursOk = summary.BusinessHours.Count > 0;
        summary.Rules.DeliveryOk = summary.DeliveryAreas.Count > 0;
        summary.Rules.ProductsOk = products != null && products.Any(p => p.IsAvailable);

        int progress = 0;
        if (summary.Rules.DetailsOk) progress += 25;
        if (summary.Rules.HoursOk) progress += 20;
        if (summary.Rules.DeliveryOk) progress += 25;
        if (summary.Rules.ProductsOk) progress += 30;

        summary.CompletionPercentage = progress;
        summary.CanPublish = progress == 100;

        return summary;
    }

    public async Task<bool> PublishStoreAsync(Guid storeId, Guid ownerId, CancellationToken cancellationToken)
    {
        var summary = await GetStorePublishSummaryAsync(storeId, ownerId, cancellationToken);
        if (!summary.CanPublish)
        {
            return false;
        }

        var store = await _storeService.GetByOwnerAsync(ownerId, cancellationToken);
        if (store == null || store.Id != storeId)
        {
            return false;
        }

        await _storeService.UpdateStatusAsync(ownerId, storeId, true, null, cancellationToken);

        return true;
    }
}
