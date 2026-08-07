using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class StoreService : IStoreService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IStoreReadRepository _storeReadRepository;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IImageUploadService _imageUploadService;

    public StoreService(
        ApplicationDbContext dbContext,
        IMapper mapper,
        IStoreReadRepository storeReadRepository,
        IEfUnitOfWork efUnitOfWork,
        IImageUploadService imageUploadService)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _storeReadRepository = storeReadRepository;
        _efUnitOfWork = efUnitOfWork;
        _imageUploadService = imageUploadService;
    }

    public async Task<(bool Created, bool AlreadyExists, bool InvalidCuisineType, StoreResponseDto? Store)> CreateForOwnerAsync(
        Guid ownerUserId,
        CreateStoreRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var existingStore = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerUserId == ownerUserId, cancellationToken);

        if (existingStore is not null)
        {
            Serilog.Log.Warning("{EventType} | Store creation failed | OwnerUserId={OwnerUserId} | Reason=already_exists | IP={IpAddress}", "STORE_CREATE_FAILED", ownerUserId, ipAddress);
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreCreateFailed",
                nameof(Store),
                existingStore.Id,
                "Store creation blocked: seller already has a store.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return (false, true, false, null);
        }

        var normalizedCuisineType = request.CuisineType.Trim().ToLowerInvariant();
        var cuisine = await _dbContext.CuisineTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IsActive && x.Name.ToLower() == normalizedCuisineType,
                cancellationToken);

        if (cuisine is null)
        {
            Serilog.Log.Warning("{EventType} | Store creation failed | OwnerUserId={OwnerUserId} | Reason=invalid_cuisine | IP={IpAddress}", "STORE_CREATE_FAILED", ownerUserId, ipAddress);
            return (false, false, true, null);
        }

        var slug = await GenerateUniqueSlugAsync(request.Slug?.Trim(), request.Name.Trim(), cancellationToken);

        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = request.Name.Trim(),
            Slug = slug,
            PhoneNumber = request.PhoneNumber.Trim(),
            Document = NormalizeDocument(request.Document),
            PixKey = NormalizeOptional(request.PixKey, 50),
            InstagramUrl = NormalizeOptional(request.InstagramUrl, 500),
            FacebookUrl = NormalizeOptional(request.FacebookUrl, 500),
            TikTokUrl = NormalizeOptional(request.TikTokUrl, 500),
            WebsiteUrl = NormalizeOptional(request.WebsiteUrl, 500),
            Description = request.Description.Trim(),
            CuisineTypeId = cuisine.Id,

            BannerUrl = request.BannerUrl?.Trim(),
            LogoUrl = request.LogoUrl?.Trim(),
            IsOpen = false,
            IsSubscriptionBlocked = false,
            SupportsDelivery = request.SupportsDelivery,
            SupportsPickup = request.SupportsPickup,

            InitialMinute = request.InitialMinute,
            FinalMinute = request.FinalMinute,
            MaxDeliveryRadiusKm = request.MaxDeliveryRadiusKm,

            DeliveryFee = 0,
            MinimumOrderValue = 0
        };

        await _dbContext.Stores.AddAsync(store, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(
            ownerUserId,
            "StoreCreated",
            nameof(Store),
            store.Id,
            "Store created successfully.",
            ipAddress,
            cancellationToken);

        Serilog.Log.Information("{EventType} | Store created | StoreId={StoreId} | OwnerUserId={OwnerUserId} | Name={Name} | CuisineType={CuisineType} | IP={IpAddress}",
            "STORE_CREATED", store.Id, ownerUserId, store.Name, store.CuisineType, ipAddress);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return (true, false, false, _mapper.Map<StoreResponseDto>(store));
    }

    public async Task<StoreResponseDto?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _storeReadRepository.GetByOwnerAsync(ownerUserId, cancellationToken);
    }

    public async Task<UpdateStoreResultDto> UpdateAsync(
        Guid ownerUserId,
        Guid storeId,
        UpdateStoreRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            Serilog.Log.Warning("{EventType} | Store update failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=not_found | IP={IpAddress}", "STORE_UPDATE_FAILED", ownerUserId, storeId, ipAddress);
            return new UpdateStoreResultDto
            {
                NotFound = true
            };
        }

        if (store.OwnerUserId != ownerUserId)
        {
            Serilog.Log.Warning("{EventType} | Store update forbidden | OwnerUserId={OwnerUserId} | StoreId={StoreId} | IP={IpAddress}", "STORE_UPDATE_FORBIDDEN", ownerUserId, storeId, ipAddress);
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreUpdateForbidden",
                nameof(Store),
                store.Id,
                "Store update denied: user is not the owner.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return new UpdateStoreResultDto
            {
                Forbidden = true
            };
        }

        var normalizedCuisineType = request.CuisineType.Trim().ToLowerInvariant();
        var cuisine = await _dbContext.CuisineTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IsActive && x.Name.ToLower() == normalizedCuisineType,
                cancellationToken);

        if (cuisine is null)
        {
            Serilog.Log.Warning("{EventType} | Store update failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=invalid_cuisine | IP={IpAddress}", "STORE_UPDATE_FAILED", ownerUserId, storeId, ipAddress);
            return new UpdateStoreResultDto
            {
                InvalidCuisineType = true
            };
        }

        var nameChanged = !string.Equals(store.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase);
        store.Name = request.Name.Trim();
        store.CuisineTypeId = cuisine.Id;

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var slug = await GenerateUniqueSlugAsync(request.Slug.Trim(), request.Name.Trim(), cancellationToken, store.Id);
            store.Slug = slug;
        }

        store.PhoneNumber = request.PhoneNumber.Trim();
        store.Document = NormalizeDocument(request.Document);
        store.PixKey = NormalizeOptional(request.PixKey, 50);
        store.InstagramUrl = NormalizeOptional(request.InstagramUrl, 500);
        store.FacebookUrl = NormalizeOptional(request.FacebookUrl, 500);
        store.TikTokUrl = NormalizeOptional(request.TikTokUrl, 500);
        store.WebsiteUrl = NormalizeOptional(request.WebsiteUrl, 500);
        store.Description = request.Description.Trim();

        if (!string.IsNullOrWhiteSpace(store.LogoUrl) && store.LogoUrl != request.LogoUrl?.Trim())
        {
            try { await _imageUploadService.DeleteAsync(store.LogoUrl, cancellationToken); } catch { }
        }
        if (!string.IsNullOrWhiteSpace(store.BannerUrl) && store.BannerUrl != request.BannerUrl?.Trim())
        {
            try { await _imageUploadService.DeleteAsync(store.BannerUrl, cancellationToken); } catch { }
        }

        store.BannerUrl = request.BannerUrl?.Trim();
        store.LogoUrl = request.LogoUrl?.Trim();
        store.SupportsDelivery = request.SupportsDelivery;
        store.SupportsPickup = request.SupportsPickup;
        store.InitialMinute = request.InitialMinute;
        store.FinalMinute = request.FinalMinute;
        store.MaxDeliveryRadiusKm = request.MaxDeliveryRadiusKm;

        store.MarkAsUpdated();

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(
            ownerUserId,
            "StoreUpdated",
            nameof(Store),
            store.Id,
            "Store updated successfully.",
            ipAddress,
            cancellationToken);

        Serilog.Log.Information("{EventType} | Store updated | StoreId={StoreId} | OwnerUserId={OwnerUserId} | Name={Name} | CuisineType={CuisineType} | IP={IpAddress}",
            "STORE_UPDATED", store.Id, ownerUserId, store.Name, store.CuisineType, ipAddress);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateStoreResultDto
        {
            Store = _mapper.Map<StoreResponseDto>(store)
        };
    }

    private static string? NormalizeDocument(string? document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;
        return new string(document.Where(char.IsDigit).ToArray());
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
    }

    public async Task<UpdateStoreResultDto> UpdateStatusAsync(
        Guid ownerUserId,
        Guid storeId,
        bool isOpen,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            Serilog.Log.Warning("{EventType} | Store status update failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=not_found | IP={IpAddress}", "STORE_STATUS_UPDATE_FAILED", ownerUserId, storeId, ipAddress);
            return new UpdateStoreResultDto
            {
                NotFound = true
            };
        }

        if (store.OwnerUserId != ownerUserId)
        {
            Serilog.Log.Warning("{EventType} | Store status update forbidden | OwnerUserId={OwnerUserId} | StoreId={StoreId} | IP={IpAddress}", "STORE_STATUS_UPDATE_FORBIDDEN", ownerUserId, storeId, ipAddress);
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreStatusUpdateForbidden",
                nameof(Store),
                store.Id,
                "Store status update denied: user is not the owner.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return new UpdateStoreResultDto
            {
                Forbidden = true
            };
        }

        if (isOpen && store.IsSubscriptionBlocked)
        {
            Serilog.Log.Warning("{EventType} | Store status update blocked by subscription | OwnerUserId={OwnerUserId} | StoreId={StoreId} | IP={IpAddress}", "STORE_STATUS_UPDATE_BLOCKED", ownerUserId, storeId, ipAddress);
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreStatusUpdateBlockedBySubscription",
                nameof(Store),
                store.Id,
                "Store status update denied: subscription is overdue/blocked.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return new UpdateStoreResultDto
            {
                SubscriptionBlocked = true,
                Store = _mapper.Map<StoreResponseDto>(store)
            };
        }

        store.IsOpen = isOpen;
        store.MarkAsUpdated();
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(
            ownerUserId,
            "StoreStatusUpdated",
            nameof(Store),
            store.Id,
            $"Store status changed to {(isOpen ? "open" : "closed")}",
            ipAddress,
            cancellationToken);

        Serilog.Log.Information("{EventType} | Store status updated | StoreId={StoreId} | OwnerUserId={OwnerUserId} | IsOpen={IsOpen} | IP={IpAddress}",
            "STORE_STATUS_UPDATED", store.Id, ownerUserId, isOpen, ipAddress);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateStoreResultDto
        {
            Store = _mapper.Map<StoreResponseDto>(store)
        };
    }

    public async Task<UpdateStoreResultDto> UpdateDeliveryConfigAsync(
        Guid ownerUserId,
        Guid storeId,
        decimal deliveryFee,
        decimal minimumOrderValue,
        decimal? freeShippingThreshold,
        bool freeShippingToday,
        IEnumerable<StoreDeliveryAreaDto>? deliveryAreas,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            Serilog.Log.Warning("{EventType} | Store delivery config update failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=not_found | IP={IpAddress}", "STORE_DELIVERY_CONFIG_UPDATE_FAILED", ownerUserId, storeId, ipAddress);
            return new UpdateStoreResultDto
            {
                NotFound = true
            };
        }

        if (store.OwnerUserId != ownerUserId)
        {
            Serilog.Log.Warning("{EventType} | Store delivery config update forbidden | OwnerUserId={OwnerUserId} | StoreId={StoreId} | IP={IpAddress}", "STORE_DELIVERY_CONFIG_UPDATE_FORBIDDEN", ownerUserId, storeId, ipAddress);
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreDeliveryConfigUpdateForbidden",
                nameof(Store),
                store.Id,
                "Store delivery config update denied: user is not the owner.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return new UpdateStoreResultDto
            {
                Forbidden = true
            };
        }

        store.DeliveryFee = deliveryFee;
        store.MinimumOrderValue = minimumOrderValue;
        store.FreeShippingThreshold = freeShippingThreshold;
        store.FreeShippingToday = freeShippingToday;

        if (deliveryAreas is not null)
        {
            var isRelational = _dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";
            if (isRelational)
            {
                await _dbContext.Set<StoreDeliveryArea>()
                    .Where(x => x.StoreId == storeId)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                var existing = await _dbContext.Set<StoreDeliveryArea>()
                    .Where(x => x.StoreId == storeId)
                    .ToListAsync(cancellationToken);
                _dbContext.Set<StoreDeliveryArea>().RemoveRange(existing);
            }

            foreach (var area in deliveryAreas)
            {
                _dbContext.Set<StoreDeliveryArea>().Add(new StoreDeliveryArea
                {
                    StoreId = storeId,
                    Neighborhood = area.Neighborhood,
                    DeliveryFee = area.DeliveryFee,
                    MinimumOrderValue = area.MinimumOrderValue,
                    FreeShippingThreshold = area.FreeShippingThreshold,
                    IsActive = area.IsActive,
                    Notes = area.Notes.Trim()
                });
            }
        }

        store.MarkAsUpdated();

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(
            ownerUserId,
            "StoreDeliveryConfigUpdated",
            nameof(Store),
            store.Id,
            "Store delivery fee and minimum order updated.",
            ipAddress,
            cancellationToken);

        Serilog.Log.Information("{EventType} | Store delivery config updated | StoreId={StoreId} | OwnerUserId={OwnerUserId} | DeliveryFee={DeliveryFee} | MinimumOrderValue={MinimumOrderValue} | IP={IpAddress}",
            "STORE_DELIVERY_CONFIG_UPDATED", store.Id, ownerUserId, deliveryFee, minimumOrderValue, ipAddress);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateStoreResultDto
        {
            Store = _mapper.Map<StoreResponseDto>(store)
        };
    }

    public async Task<IReadOnlyCollection<DeliveryTimeResponseDto>> GetActiveDeliveryTimesAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<DeliveryTime>()
            .AsNoTracking()
            .Where(x => x.IsActive && x.StoreId == storeId)
            .OrderBy(x => x.MinTimeMinutes)
            .Select(x => new DeliveryTimeResponseDto
            {
                Id = x.Id,
                MinTimeMinutes = x.MinTimeMinutes,
                MaxTimeMinutes = x.MaxTimeMinutes,
                FormattedTime = x.FormattedTime
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DeliveryTimeResponseDto?> CreateDeliveryTimeAsync(Guid storeId, int minTimeMinutes, int maxTimeMinutes, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Set<DeliveryTime>()
            .AsNoTracking()
            .AnyAsync(x => x.StoreId == storeId && x.MinTimeMinutes == minTimeMinutes && x.MaxTimeMinutes == maxTimeMinutes && x.IsActive, cancellationToken);

        if (exists)
            return null;

        var dt = new DeliveryTime
        {
            StoreId = storeId,
            MinTimeMinutes = minTimeMinutes,
            MaxTimeMinutes = maxTimeMinutes,
            IsActive = true
        };

        await _dbContext.Set<DeliveryTime>().AddAsync(dt, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new DeliveryTimeResponseDto
        {
            Id = dt.Id,
            MinTimeMinutes = dt.MinTimeMinutes,
            MaxTimeMinutes = dt.MaxTimeMinutes,
            FormattedTime = dt.FormattedTime
        };
    }

    public async Task<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>> GetActiveDeliveryNeighborhoodsAsync(string city, CancellationToken cancellationToken = default)
    {
        return await GetNeighborhoodsByCityAsync(city, cancellationToken);
    }

    public async Task<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>> GetActiveDeliveryNeighborhoodsByStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
            return Array.Empty<DeliveryNeighborhoodResponseDto>();

        if (store.MaxDeliveryRadiusKm is null or <= 0)
        {
            var addr = await _dbContext.StoreAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);
            return addr?.City is not null
                ? await GetNeighborhoodsByCityAsync(addr.City, cancellationToken)
                : Array.Empty<DeliveryNeighborhoodResponseDto>();
        }

        var storeAddr = await _dbContext.StoreAddresses
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);

        if (storeAddr?.Latitude is null || storeAddr.Longitude is null)
        {
            return storeAddr?.City is not null
                ? await GetNeighborhoodsByCityAsync(storeAddr.City, cancellationToken)
                : Array.Empty<DeliveryNeighborhoodResponseDto>();
        }
        var maxRadius = store.MaxDeliveryRadiusKm.Value;
        var allNeighborhoods = await _dbContext.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.IsActive && x.Latitude != null && x.Longitude != null)
            .OrderBy(x => x.Neighborhood)
            .Select(x => new DeliveryNeighborhoodResponseDto
            {
                Id = x.Id,
                Neighborhood = x.Neighborhood,
                NormalizedName = x.NormalizedName,
                City = x.City,
                CityId = x.CityId,
                OsmId = x.OsmId,
                OsmType = x.OsmType,
                PlaceType = x.PlaceType,
                Latitude = x.Latitude,
                Longitude = x.Longitude,
                Source = x.Source,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        var storeLat = storeAddr.Latitude.Value;
        var storeLon = storeAddr.Longitude.Value;

        var withinRadius = allNeighborhoods
            .Where(x => HaversineKm(storeLat, storeLon, x.Latitude!.Value, x.Longitude!.Value) <= maxRadius)
            .ToList();

        if (withinRadius.Count > 0)
            return withinRadius;

        return storeAddr.City is not null
            ? await GetNeighborhoodsByCityAsync(storeAddr.City, cancellationToken)
            : Array.Empty<DeliveryNeighborhoodResponseDto>();
    }

    private async Task<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>> GetNeighborhoodsByCityAsync(string city, CancellationToken cancellationToken)
    {
        return await _dbContext.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.IsActive && x.City.ToLower() == city.ToLower().Trim())
            .OrderBy(x => x.Neighborhood)
            .Select(x => new DeliveryNeighborhoodResponseDto
            {
                Id = x.Id,
                Neighborhood = x.Neighborhood,
                NormalizedName = x.NormalizedName,
                City = x.City,
                CityId = x.CityId,
                OsmId = x.OsmId,
                OsmType = x.OsmType,
                PlaceType = x.PlaceType,
                Latitude = x.Latitude,
                Longitude = x.Longitude,
                Source = x.Source,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    public async Task<DeliveryNeighborhoodResponseDto?> CreateDeliveryNeighborhoodAsync(string neighborhood, string city, CancellationToken cancellationToken = default)
    {
        var normalizedNeighborhood = neighborhood.Trim();
        var normalizedCity = city.Trim();

        var exists = await _dbContext.DeliveryNeighborhoods
            .AsNoTracking()
            .AnyAsync(x => x.IsActive
                && x.Neighborhood.ToLower() == normalizedNeighborhood.ToLower()
                && x.City.ToLower() == normalizedCity.ToLower(), cancellationToken);

        if (exists)
            return null;

        var dn = new DeliveryNeighborhood
        {
            Neighborhood = normalizedNeighborhood,
            NormalizedName = NormalizeText(normalizedNeighborhood),
            City = normalizedCity,
            IsActive = true
        };

        await _dbContext.DeliveryNeighborhoods.AddAsync(dn, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new DeliveryNeighborhoodResponseDto
        {
            Id = dn.Id,
            Neighborhood = dn.Neighborhood,
            NormalizedName = dn.NormalizedName,
            City = dn.City,
            CityId = dn.CityId,
            IsActive = dn.IsActive
        };
    }

    private static string NormalizeText(string text)
    {
        return text
            .ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c))
            .ToString()
            .Replace('\t', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();
    }

    private async Task<string> GenerateUniqueSlugAsync(string? requestedSlug, string storeName, CancellationToken cancellationToken, Guid? excludeStoreId = null)
    {
        var baseSlug = string.IsNullOrWhiteSpace(requestedSlug)
            ? Slugify(storeName)
            : requestedSlug;

        var slug = baseSlug;
        var suffix = 1;
        while (await _dbContext.Stores.AnyAsync(x =>
            x.Slug == slug &&
            (!excludeStoreId.HasValue || x.Id != excludeStoreId.Value), cancellationToken))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static string Slugify(string value)
    {
        var slug = value.ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in slug)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        slug = sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-");
        return slug.Trim('-');
    }

    private async Task WriteAuditLogAsync(
        Guid userId,
        string auditEvent,
        string entity,
        Guid entityId,
        string description,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = userId,
            Event = auditEvent,
            Entity = entity,
            EntityId = entityId,
            Description = description,
            IpAddress = ipAddress
        }, cancellationToken);
    }

}


