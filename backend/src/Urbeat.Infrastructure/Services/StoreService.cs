using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public async Task<(bool Created, bool AlreadyExists, bool InvalidCuisineType, bool SlugConflict, StoreResponseDto? Store)> CreateForOwnerAsync(
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

            return (false, true, false, false, null);
        }

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Slugify(request.Name.Trim())
            : request.Slug.Trim();

        if (await _dbContext.Stores
            .AsNoTracking()
            .AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            Serilog.Log.Warning("{EventType} | Store creation failed | OwnerUserId={OwnerUserId} | Reason=slug_conflict | Slug={Slug} | IP={IpAddress}", "STORE_CREATE_FAILED", ownerUserId, slug, ipAddress);
            return (false, false, false, true, null);
        }

        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = request.Name.Trim(),
            Slug = slug,
            PhoneNumber = request.PhoneNumber.Trim(),
            Document = NormalizeDocument(request.Document),
            PixKey = NormalizeOptional(request.PixKey, 50),
            WebsiteUrl = NormalizeOptional(request.WebsiteUrl, 500),

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

        // Um padrão protegido pode ser selecionado; qualquer outro nome gera uma categoria
        // privada desta loja na mesma unidade de trabalho. Categorias privadas de outra loja
        // nunca são aceitas nem reutilizadas.
        var cuisineName = request.CuisineType?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cuisineName))
        {
            Serilog.Log.Warning("{EventType} | Store creation failed | OwnerUserId={OwnerUserId} | Reason=invalid_cuisine | IP={IpAddress}", "STORE_CREATE_FAILED", ownerUserId, ipAddress);
            return (false, false, true, false, null);
        }

        var normalizedCuisineType = CuisineType.NormalizeName(cuisineName);
        var defaultCuisine = await _dbContext.CuisineTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IsActive && x.IsDefault && x.StoreId == null && x.NormalizedName == normalizedCuisineType,
                cancellationToken);

        if (defaultCuisine is not null)
        {
            store.CuisineTypeId = defaultCuisine.Id;
        }
        else if (CuisineTypeDefaults.IsDefaultNormalizedName(normalizedCuisineType))
        {
            // Nome de padrão protegido sem registro ativo disponível: não cria cópia privada.
            Serilog.Log.Warning("{EventType} | Store creation failed | OwnerUserId={OwnerUserId} | Reason=invalid_cuisine | IP={IpAddress}", "STORE_CREATE_FAILED", ownerUserId, ipAddress);
            return (false, false, true, false, null);
        }
        else
        {
            var privateCuisine = new CuisineType
            {
                Name = cuisineName,
                IsActive = true,
                IsDefault = false,
                StoreId = store.Id
            };
            _dbContext.CuisineTypes.Add(privateCuisine);
            store.CuisineTypeId = privateCuisine.Id;
        }

        await _dbContext.Stores.AddAsync(store, cancellationToken);

        await AttachDefaultSubscriptionAsync(store, ownerUserId, cancellationToken);

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsStoreSlugUniqueViolation(exception))
        {
            // A concurrent request persisted the same slug between the pre-check above and this
            // save; the unique index is the real guard. Report it as a slug conflict instead of a 500.
            Serilog.Log.Warning("{EventType} | Store creation failed | OwnerUserId={OwnerUserId} | Reason=slug_conflict | Slug={Slug} | IP={IpAddress}", "STORE_CREATE_FAILED", ownerUserId, store.Slug, ipAddress);
            return (false, false, false, true, null);
        }

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

        return (true, false, false, false, _mapper.Map<StoreResponseDto>(store));
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

        // Store update accepts a global protected default or a category owned by this store.
        // Matching uses the canonical NormalizedName and never selects another store's private category.
        var cuisineName = request.CuisineType?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cuisineName))
        {
            Serilog.Log.Warning("{EventType} | Store update failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=invalid_cuisine | IP={IpAddress}", "STORE_UPDATE_FAILED", ownerUserId, storeId, ipAddress);
            return new UpdateStoreResultDto
            {
                InvalidCuisineType = true
            };
        }

        var normalizedCuisineType = CuisineType.NormalizeName(cuisineName);
        var cuisine = await _dbContext.CuisineTypes
            .AsNoTracking()
            .Where(x => x.IsActive
                && x.NormalizedName == normalizedCuisineType
                && ((x.IsDefault && x.StoreId == null) || x.StoreId == store.Id))
            .OrderByDescending(x => x.StoreId == store.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (cuisine is null)
        {
            Serilog.Log.Warning("{EventType} | Store update failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=invalid_cuisine | IP={IpAddress}", "STORE_UPDATE_FAILED", ownerUserId, storeId, ipAddress);
            return new UpdateStoreResultDto
            {
                InvalidCuisineType = true
            };
        }

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var requestedSlug = request.Slug.Trim();
            var slugConflict = await _dbContext.Stores
                .AsNoTracking()
                .AnyAsync(x => x.Slug == requestedSlug && x.Id != store.Id, cancellationToken);

            if (slugConflict)
            {
                Serilog.Log.Warning("{EventType} | Store update failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=slug_conflict | Slug={Slug} | IP={IpAddress}", "STORE_UPDATE_FAILED", ownerUserId, storeId, requestedSlug, ipAddress);
                return new UpdateStoreResultDto
                {
                    SlugConflict = true
                };
            }
        }

        store.Name = request.Name.Trim();
        store.CuisineTypeId = cuisine.Id;

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            store.Slug = request.Slug.Trim();
        }

        store.PhoneNumber = request.PhoneNumber.Trim();
        store.Document = NormalizeDocument(request.Document);
        store.PixKey = NormalizeOptional(request.PixKey, 50);
        store.WebsiteUrl = NormalizeOptional(request.WebsiteUrl, 500);

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

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsStoreSlugUniqueViolation(exception))
        {
            // A concurrent request persisted the same slug between the pre-check above and this
            // save; the unique index is the real guard. Report it as a slug conflict instead of a 500.
            Serilog.Log.Warning("{EventType} | Store update failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=slug_conflict | Slug={Slug} | IP={IpAddress}", "STORE_UPDATE_FAILED", ownerUserId, storeId, store.Slug, ipAddress);
            return new UpdateStoreResultDto
            {
                SlugConflict = true
            };
        }

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

    public async Task<UpdateStoreResultDto> MarkAsPublishedAsync(
        Guid ownerUserId,
        Guid storeId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            Serilog.Log.Warning("{EventType} | Store publish failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=not_found | IP={IpAddress}", "STORE_PUBLISH_FAILED", ownerUserId, storeId, ipAddress);
            return new UpdateStoreResultDto
            {
                NotFound = true
            };
        }

        if (store.OwnerUserId != ownerUserId)
        {
            Serilog.Log.Warning("{EventType} | Store publish forbidden | OwnerUserId={OwnerUserId} | StoreId={StoreId} | IP={IpAddress}", "STORE_PUBLISH_FORBIDDEN", ownerUserId, storeId, ipAddress);
            await WriteAuditLogAsync(
                ownerUserId,
                "StorePublishForbidden",
                nameof(Store),
                store.Id,
                "Store publish denied: user is not the owner.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return new UpdateStoreResultDto
            {
                Forbidden = true
            };
        }

        store.IsPublished = true;
        store.MarkAsUpdated();
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(
            ownerUserId,
            "StorePublished",
            nameof(Store),
            store.Id,
            "Store published successfully.",
            ipAddress,
            cancellationToken);

        Serilog.Log.Information("{EventType} | Store published | StoreId={StoreId} | OwnerUserId={OwnerUserId} | IP={IpAddress}",
            "STORE_PUBLISHED", store.Id, ownerUserId, ipAddress);

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
        double? maxDeliveryRadiusKm = null,
        string? ipAddress = null,
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
        store.FreeShippingTodayDate = freeShippingToday
            ? StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow)
            : null;

        if (maxDeliveryRadiusKm is > 0)
        {
            store.MaxDeliveryRadiusKm = maxDeliveryRadiusKm.Value;
        }

        if (deliveryAreas is not null)
        {
            var submittedAreas = deliveryAreas.ToList();

            var effectiveRadiusKm = maxDeliveryRadiusKm is > 0
                ? maxDeliveryRadiusKm
                : store.MaxDeliveryRadiusKm;

            // Server-side guard: a radius reduction/change must not retain store delivery areas
            // outside the radius, even when a stale or direct client submits them. Eligibility is
            // derived from the same radius/city/manual rules used to list neighborhoods; global
            // DeliveryNeighborhood rows are never modified. Stores without an address cannot apply
            // the radius rules, so their submitted areas keep the previous behavior.
            var storeAddress = await _dbContext.StoreAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);

            if (effectiveRadiusKm is > 0 && storeAddress is not null)
            {
                var eligible = await GetActiveDeliveryNeighborhoodsByStoreAsync(storeId, effectiveRadiusKm, cancellationToken);
                var eligibleNames = eligible
                    .Select(x => NormalizeText(x.Neighborhood))
                    .ToHashSet();

                // Only store associations whose names match a global DeliveryNeighborhood in
                // the store's city are subject to the radius rules. Manual store-only
                // associations without a global record cannot be classified by distance and
                // must be kept. Global DeliveryNeighborhood rows themselves are never modified.
                var globalNamesQuery = _dbContext.DeliveryNeighborhoods
                    .AsNoTracking()
                    .Where(x => x.IsActive);

                if (storeAddress.City is not null)
                {
                    var storeCity = storeAddress.City.ToLower().Trim();
                    globalNamesQuery = globalNamesQuery.Where(x => x.City.ToLower() == storeCity);
                }

                var knownGlobalNames = (await globalNamesQuery
                        .Select(x => x.Neighborhood)
                        .ToListAsync(cancellationToken))
                    .Select(NormalizeText)
                    .ToHashSet();

                submittedAreas = submittedAreas
                    .Where(x =>
                    {
                        var normalized = NormalizeText(x.Neighborhood);
                        return !knownGlobalNames.Contains(normalized) || eligibleNames.Contains(normalized);
                    })
                    .ToList();
            }

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

            foreach (var area in submittedAreas)
            {
                _dbContext.Set<StoreDeliveryArea>().Add(new StoreDeliveryArea
                {
                    StoreId = storeId,
                    Neighborhood = area.Neighborhood,
                    DeliveryFee = area.DeliveryFee,
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

    public async Task<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>> GetActiveDeliveryNeighborhoodsByStoreAsync(Guid storeId, double? radiusKm = null, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
            return Array.Empty<DeliveryNeighborhoodResponseDto>();

        var effectiveRadiusKm = radiusKm ?? store.MaxDeliveryRadiusKm;

        if (effectiveRadiusKm is null or <= 0)
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
        var maxRadius = effectiveRadiusKm.Value;

        var neighborhoodsQuery = _dbContext.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.IsActive && x.Latitude != null && x.Longitude != null);

        if (storeAddr.City is not null)
        {
            var city = storeAddr.City.ToLower().Trim();
            neighborhoodsQuery = neighborhoodsQuery.Where(x => x.City.ToLower() == city);
        }

        var allNeighborhoods = await neighborhoodsQuery
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

        // Bairros manuais (sem Latitude/Longitude) pertencem à cidade da loja e
        // não podem ser filtrados por raio; devem continuar sendo retornados.
        List<DeliveryNeighborhoodResponseDto> manualNeighborhoods;
        if (storeAddr.City is not null)
        {
            manualNeighborhoods = await _dbContext.DeliveryNeighborhoods
                .AsNoTracking()
                .Where(x => x.IsActive
                    && (x.Latitude == null || x.Longitude == null)
                    && x.City.ToLower() == storeAddr.City.ToLower().Trim())
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
        else
        {
            manualNeighborhoods = [];
        }

        var result = manualNeighborhoods
            .Concat(withinRadius)
            .OrderBy(x => x.Neighborhood)
            .ToList();

        // A valid positive radius with coordinates must honor the radius even
        // when it yields no eligible neighborhoods: returning the full city
        // here would make shrinking the radius unable to remove store areas.
        // The city fallback only applies to missing/zero radius or missing
        // coordinates, which are handled above.
        return result;
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

    private async Task AttachDefaultSubscriptionAsync(Store store, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.Plans
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Name == BillingPlanSeeder.DefaultPlanName, cancellationToken);

        var planId = plan?.Id;
        var planName = plan?.Name ?? BillingPlanSeeder.DefaultPlanName;
        var planAmount = plan?.Amount ?? BillingPlanSeeder.DefaultPlanAmount;

        var now = DateTime.UtcNow;
        var nextBillingDateUtc = now.AddMonths(1);

        await _dbContext.SellerSubscriptions.AddAsync(new SellerSubscription
        {
            StoreId = store.Id,
            SellerUserId = ownerUserId,
            PlanId = planId,
            PlanName = planName,
            PlanAmount = planAmount,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = now,
            NextBillingDateUtc = nextBillingDateUtc,
            GatewayCustomerId = string.Empty,
            GatewaySubscriptionId = string.Empty
        }, cancellationToken);

        var status = await _dbContext.SellerSubscriptionStatuses
            .SingleOrDefaultAsync(x => x.SellerUserId == ownerUserId, cancellationToken);

        if (status is null)
        {
            await _dbContext.SellerSubscriptionStatuses.AddAsync(new SellerSubscriptionStatus
            {
                SellerUserId = ownerUserId,
                NextDueDateUtc = nextBillingDateUtc,
                BillingStatus = SellerSubscriptionBillingStatus.Active
            }, cancellationToken);
        }
        else
        {
            status.BillingStatus = SellerSubscriptionBillingStatus.Active;
            status.NextDueDateUtc = nextBillingDateUtc;
            status.MarkAsUpdated();
        }
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

    private static bool IsStoreSlugUniqueViolation(DbUpdateException exception)
    {
        // PostgreSQL raises SQLSTATE 23505 when a concurrent request persists the same slug between
        // the pre-check and SaveChanges. Only the Stores.IX_Stores_Slug unique index maps to a slug
        // conflict; any other DbUpdateException (including other unique indexes on Stores) propagates.
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } pg && pg.MessageText.Contains("IX_Stores_Slug", StringComparison.OrdinalIgnoreCase);
    }

}


