using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Geo;
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
        CuisineType? privateCuisine = null;
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
            privateCuisine = new CuisineType
            {
                Name = cuisineName,
                IsActive = true,
                IsDefault = false
            };
        }

        await _dbContext.Stores.AddAsync(store, cancellationToken);

        await AttachDefaultSubscriptionAsync(store, ownerUserId, cancellationToken);

        // Stores.CuisineTypeId -> CuisineTypes and CuisineTypes.StoreId -> Stores are two
        // independent foreign keys. With both rows Added, EF cannot order the INSERTs and throws
        // a circular-dependency exception before any SQL runs. Persist the store first, then the
        // private category that points back to it, inside one transaction so a failure never
        // leaves a store without its category or a private category without its owner.
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            if (privateCuisine is not null)
            {
                await _efUnitOfWork.SaveChangesAsync(cancellationToken);

                privateCuisine.StoreId = store.Id;
                store.CuisineTypeId = privateCuisine.Id;
                _dbContext.CuisineTypes.Add(privateCuisine);

                await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            }
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

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return (true, false, false, false, _mapper.Map<StoreResponseDto>(store));
    }

    public async Task<StoreResponseDto?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _storeReadRepository.GetByOwnerAsync(ownerUserId, cancellationToken);
    }

    public async Task<DeleteStoreResultDto> DeleteForOwnerAsync(
        Guid ownerUserId,
        Guid storeId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            Serilog.Log.Warning("{EventType} | Store deletion failed | OwnerUserId={OwnerUserId} | StoreId={StoreId} | Reason=not_found | IP={IpAddress}", "STORE_DELETE_FAILED", ownerUserId, storeId, ipAddress);
            return new DeleteStoreResultDto
            {
                NotFound = true
            };
        }

        if (store.OwnerUserId != ownerUserId)
        {
            Serilog.Log.Warning("{EventType} | Store deletion forbidden | OwnerUserId={OwnerUserId} | StoreId={StoreId} | IP={IpAddress}", "STORE_DELETE_FORBIDDEN", ownerUserId, storeId, ipAddress);
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreDeleteForbidden",
                nameof(Store),
                store.Id,
                "Store deletion denied: user is not the owner.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return new DeleteStoreResultDto
            {
                Forbidden = true
            };
        }

        // Collect the Cloudinary URLs while the rows still exist. The database mutation below is
        // the source of truth; remote deletion happens only after the commit succeeds.
        var assets = await CollectStoreImageUrlsAsync(store, cancellationToken);

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            await DeleteStoreDependentsAsync(store, cancellationToken);

            _dbContext.Stores.Remove(store);
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent deletion removed the store between the read and the delete. The
            // transaction is rolled back by disposal; report the same 404 as an already-deleted
            // store so the endpoint stays idempotent.
            Serilog.Log.Warning("{EventType} | Store deletion raced with another deletion | OwnerUserId={OwnerUserId} | StoreId={StoreId} | IP={IpAddress}", "STORE_DELETE_FAILED", ownerUserId, storeId, ipAddress);
            return new DeleteStoreResultDto
            {
                NotFound = true
            };
        }
        catch (Exception exception)
        {
            Serilog.Log.Error(exception, "{EventType} | Store deletion rolled back | OwnerUserId={OwnerUserId} | StoreId={StoreId} | IP={IpAddress}", "STORE_DELETE_FAILED", ownerUserId, storeId, ipAddress);
            throw;
        }

        // Best-effort Cloudinary cleanup after commit. A remote failure must never make the
        // already-committed database deletion look unsuccessful, and request cancellation must not
        // leave a half-cleaned set with no audit entry.
        var deletedAssets = 0;
        var failedAssets = 0;
        foreach (var imageUrl in assets)
        {
            try
            {
                await _imageUploadService.DeleteAsync(imageUrl, CancellationToken.None);
                deletedAssets++;
            }
            catch (Exception exception)
            {
                failedAssets++;
                Serilog.Log.Error(
                    exception,
                    "{EventType} | Store image deletion failed after commit | StoreId={StoreId} | ImageUrl={ImageUrl}",
                    "STORE_DELETE_ASSET_FAILED",
                    storeId,
                    imageUrl);
            }
        }

        try
        {
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreDeleted",
                nameof(Store),
                storeId,
                $"Store deleted. Cloudinary assets attempted={assets.Count}, deleted={deletedAssets}, failed={failedAssets}.",
                ipAddress,
                CancellationToken.None);

            await _efUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The store is already gone; audit persistence is secondary and must not turn the
            // successful deletion into a 500.
            Serilog.Log.Error(exception, "{EventType} | Store deletion audit failed | StoreId={StoreId}", "STORE_DELETE_AUDIT_FAILED", storeId);
        }

        Serilog.Log.Information(
            "{EventType} | Store deleted | StoreId={StoreId} | OwnerUserId={OwnerUserId} | Assets={Assets} | AssetsDeleted={AssetsDeleted} | AssetsFailed={AssetsFailed} | IP={IpAddress}",
            "STORE_DELETED",
            storeId,
            ownerUserId,
            assets.Count,
            deletedAssets,
            failedAssets,
            ipAddress);

        return new DeleteStoreResultDto
        {
            Deleted = true,
            AssetsAttempted = assets.Count,
            AssetsDeleted = deletedAssets,
            AssetsFailed = failedAssets
        };
    }

    private async Task<IReadOnlyList<string>> CollectStoreImageUrlsAsync(Store store, CancellationToken cancellationToken)
    {
        var urls = new List<string>();

        if (!string.IsNullOrWhiteSpace(store.LogoUrl))
        {
            urls.Add(store.LogoUrl);
        }

        if (!string.IsNullOrWhiteSpace(store.BannerUrl))
        {
            urls.Add(store.BannerUrl);
        }

        var productImageUrls = await _dbContext.Products
            .AsNoTracking()
            .Where(x => x.StoreId == store.Id && x.ImageUrl != null && x.ImageUrl != string.Empty)
            .Select(x => x.ImageUrl!)
            .ToListAsync(cancellationToken);

        urls.AddRange(productImageUrls);

        return urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Removes store-scoped rows in an order that satisfies the restrictive foreign keys
    /// (private cuisine type, product -> category and delivery route -> store driver). Global
    /// reference data (<see cref="DeliveryNeighborhood"/>, <see cref="CuisineType"/> defaults and
    /// cities) is never touched. Orders are intentionally preserved as historical records.
    /// </summary>
    private async Task DeleteStoreDependentsAsync(Store store, CancellationToken cancellationToken)
    {
        var storeId = store.Id;

        // Stores.CuisineTypeId -> CuisineTypes is Restrict. When the store points at its own
        // private category, clear the reference first; a global default category is untouched.
        if (store.CuisineTypeId is not null)
        {
            var pointsToPrivateCuisine = await _dbContext.CuisineTypes
                .AsNoTracking()
                .AnyAsync(x => x.Id == store.CuisineTypeId.Value && x.StoreId == storeId, cancellationToken);

            if (pointsToPrivateCuisine)
            {
                store.CuisineTypeId = null;
                await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        await DeleteWhereAsync(_dbContext.CuisineTypes.Where(x => x.StoreId == storeId), cancellationToken);

        // DeliveryRoute -> StoreDeliveryDriver is Restrict, so routes go first.
        await DeleteWhereAsync(_dbContext.DeliveryRoutes.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.StoreDeliveryDrivers.Where(x => x.StoreId == storeId), cancellationToken);

        // Products -> ProductCategory is Restrict, so products go first.
        await DeleteWhereAsync(_dbContext.Products.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.ProductCategories.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.ProductOptionGroupTemplates.Where(x => x.StoreId == storeId), cancellationToken);

        // StoreAdditional -> StoreAdditionalGroup is Restrict.
        var storeAdditionalIds = await _dbContext.StoreAdditionals
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (storeAdditionalIds.Count > 0)
        {
            // Product deletion already cascaded the normal assignments. Removing any residual row
            // by AdditionalId keeps an inconsistent legacy row from blocking the store deletion.
            await DeleteWhereAsync(
                _dbContext.ProductAdditionalAssignments.Where(x => storeAdditionalIds.Contains(x.AdditionalId)),
                cancellationToken);
        }

        await DeleteWhereAsync(_dbContext.StoreAdditionals.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.StoreAdditionalGroups.Where(x => x.StoreId == storeId), cancellationToken);

        await DeleteWhereAsync(_dbContext.StoreAddresses.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.StoreDeliveryAreas.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.StoreBusinessHours.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.Set<DeliveryTime>().Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.CustomerPhoneVerifications.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.StoreCustomers.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.StorePaymentGatewayConfigs.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.PaymentGatewayTransactionLogs.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.StorePrinterConfigs.Where(x => x.StoreId == storeId), cancellationToken);
        await DeleteWhereAsync(_dbContext.OrderPrintJobs.Where(x => x.StoreId == storeId), cancellationToken);

        // SellerSubscription has no FK configured but a unique StoreId; a leftover row would block
        // the seller from creating a new store.
        await DeleteWhereAsync(_dbContext.SellerSubscriptions.Where(x => x.StoreId == storeId), cancellationToken);
    }

    private async Task DeleteWhereAsync<TEntity>(IQueryable<TEntity> query, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (_dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        // The InMemory provider does not support ExecuteDelete. Track and remove instead; the
        // store deletion is saved by the caller.
        var entities = await query.ToListAsync(cancellationToken);
        if (entities.Count > 0)
        {
            _dbContext.Set<TEntity>().RemoveRange(entities);
        }
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

            var storeAddress = await _dbContext.StoreAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);

            var storeCityId = await ResolveCityIdAsync(storeAddress, cancellationToken);
            var neighborhoodLookup = await LoadNeighborhoodLookupAsync(storeAddress, storeCityId, cancellationToken);

            // Server-side guard: a radius reduction/change must not retain store delivery areas
            // outside the radius, even when a stale or direct client submits them. Eligibility uses
            // the straight-line Haversine distance over the complete circle (no 1.3 factor and no
            // route estimate). Areas that cannot be resolved to a global neighborhood keep the
            // previous behaviour: they are manual and cannot be classified by distance.
            var radiusApplied = effectiveRadiusKm is > 0
                && storeAddress?.Latitude is not null
                && storeAddress.Longitude is not null;

            var resolvedAreas = new List<(StoreDeliveryAreaDto Area, Guid? NeighborhoodId, double? DistanceKm)>(submittedAreas.Count);

            foreach (var area in submittedAreas)
            {
                var normalizedName = NeighborhoodName.Normalize(area.Neighborhood);
                if (normalizedName.Length == 0)
                {
                    continue;
                }

                var neighborhood = neighborhoodLookup.GetValueOrDefault(normalizedName);

                double? distanceKm = null;
                if (radiusApplied && neighborhood?.Latitude is not null && neighborhood.Longitude is not null)
                {
                    distanceKm = GeoDistance.HaversineKm(
                        storeAddress!.Latitude!.Value,
                        storeAddress.Longitude!.Value,
                        neighborhood.Latitude.Value,
                        neighborhood.Longitude.Value);

                    if (!GeoDistance.IsWithinRadius(distanceKm.Value, effectiveRadiusKm!.Value))
                    {
                        continue;
                    }
                }

                resolvedAreas.Add((area, neighborhood?.Id, distanceKm));
            }

            // The unique index is (StoreId, NeighborhoodId). Two submitted spellings can resolve to
            // the same global neighborhood; keep the first deterministic entry instead of failing.
            resolvedAreas = resolvedAreas
                .GroupBy(x => x.NeighborhoodId?.ToString("N") ?? NeighborhoodName.Normalize(x.Area.Neighborhood))
                .Select(group => group.First())
                .ToList();

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

            foreach (var (area, neighborhoodId, distanceKm) in resolvedAreas)
            {
                _dbContext.Set<StoreDeliveryArea>().Add(new StoreDeliveryArea
                {
                    StoreId = storeId,
                    Neighborhood = area.Neighborhood,
                    NeighborhoodId = neighborhoodId,
                    DeliveryFee = area.DeliveryFee,
                    DistanceKm = distanceKm is null ? null : GeoDistance.RoundForDisplay(distanceKm.Value, 3),
                    DistanceMethod = GeographicProvenance.CoordinateMethod.Haversine,
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

    public async Task<CreateDeliveryTimeResultDto> CreateDeliveryTimeAsync(Guid ownerUserId, Guid storeId, int minTimeMinutes, int maxTimeMinutes, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
            return new CreateDeliveryTimeResultDto { NotFound = true };

        if (store.OwnerUserId != ownerUserId)
            return new CreateDeliveryTimeResultDto { Forbidden = true };

        var exists = await _dbContext.Set<DeliveryTime>()
            .AsNoTracking()
            .AnyAsync(x => x.StoreId == storeId && x.MinTimeMinutes == minTimeMinutes && x.MaxTimeMinutes == maxTimeMinutes && x.IsActive, cancellationToken);

        if (exists)
            return new CreateDeliveryTimeResultDto { Conflict = true };

        var dt = new DeliveryTime
        {
            StoreId = storeId,
            MinTimeMinutes = minTimeMinutes,
            MaxTimeMinutes = maxTimeMinutes,
            IsActive = true
        };

        await _dbContext.Set<DeliveryTime>().AddAsync(dt, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateDeliveryTimeResultDto
        {
            DeliveryTime = new DeliveryTimeResponseDto
            {
                Id = dt.Id,
                MinTimeMinutes = dt.MinTimeMinutes,
                MaxTimeMinutes = dt.MaxTimeMinutes,
                FormattedTime = dt.FormattedTime
            }
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

        var storeAddress = await _dbContext.StoreAddresses
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);

        var cityId = await ResolveCityIdAsync(storeAddress, cancellationToken);
        var datasetVersion = await GetActiveDatasetVersionAsync(storeAddress?.State, cancellationToken);
        var effectiveRadiusKm = radiusKm ?? store.MaxDeliveryRadiusKm;

        // A radius is only applicable when it is finite, positive and the store has a coordinate.
        // Otherwise the city list is returned and no distance/eligibility is asserted.
        var radiusApplied = effectiveRadiusKm is > 0
            && double.IsFinite(effectiveRadiusKm.Value)
            && storeAddress?.Latitude is not null
            && storeAddress.Longitude is not null;

        var cityNeighborhoods = await LoadActiveCityNeighborhoodsAsync(cityId, storeAddress?.City, cancellationToken);

        if (!radiusApplied)
        {
            return cityNeighborhoods
                .Select(x => MapNeighborhood(x, datasetVersion, distanceKm: null, appliedRadiusKm: null))
                .OrderBy(x => x.Neighborhood)
                .ToList();
        }

        var storeLatitude = storeAddress!.Latitude!.Value;
        var storeLongitude = storeAddress.Longitude!.Value;
        var radius = effectiveRadiusKm!.Value;
        var boundingBox = GeoDistance.BoundingBox(storeLatitude, storeLongitude, radius);

        // Only the bounding-box candidates are loaded from PostgreSQL; a large municipality is never
        // materialized entirely just to filter by radius in memory.
        var candidateQuery = ApplyCityScope(
            _dbContext.DeliveryNeighborhoods.AsNoTracking().Where(x => x.IsActive),
            cityId,
            storeAddress.City);

        var candidates = await candidateQuery
            .Where(x => x.Latitude != null
                && x.Longitude != null
                && x.Latitude >= boundingBox.MinLatitude
                && x.Latitude <= boundingBox.MaxLatitude
                && x.Longitude >= boundingBox.MinLongitude
                && x.Longitude <= boundingBox.MaxLongitude)
            .ToListAsync(cancellationToken);

        var eligible = new List<DeliveryNeighborhoodResponseDto>(candidates.Count);

        foreach (var neighborhood in candidates)
        {
            var distanceKm = GeoDistance.HaversineKm(
                storeLatitude,
                storeLongitude,
                neighborhood.Latitude!.Value,
                neighborhood.Longitude!.Value);

            if (!GeoDistance.IsWithinRadius(distanceKm, radius))
            {
                // A valid radius with no eligible neighborhood is a valid empty result. The city
                // fallback above is only for a missing/zero radius or a store without coordinates.
                continue;
            }

            eligible.Add(MapNeighborhood(neighborhood, datasetVersion, distanceKm, radius));
        }

        // Manual neighborhoods belong to the store city, have no coordinate and are never filtered
        // by radius. They are returned separately so the seller can review them explicitly.
        var manualNeighborhoods = await ApplyCityScope(
                _dbContext.DeliveryNeighborhoods.AsNoTracking().Where(x => x.IsActive),
                cityId,
                storeAddress.City)
            .Where(x => x.Latitude == null
                || x.Longitude == null
                || x.Kind == GeographicProvenance.NeighborhoodKind.Manual)
            .ToListAsync(cancellationToken);

        var manual = manualNeighborhoods
            .Select(x => MapNeighborhood(x, datasetVersion, distanceKm: null, appliedRadiusKm: null))
            .ToList();

        return manual
            .Concat(eligible)
            .OrderBy(x => x.Neighborhood)
            .ToList();
    }

    private static IQueryable<DeliveryNeighborhood> ApplyCityScope(
        IQueryable<DeliveryNeighborhood> query,
        Guid? cityId,
        string? cityName)
    {
        var normalizedCity = string.IsNullOrWhiteSpace(cityName)
            ? null
            : cityName.ToLower().Trim();

        if (cityId is not null && normalizedCity is not null)
        {
            // Prefer the identifier, but keep the textual city as a transitional fallback for
            // legacy rows whose CityId was never reconciled. Homonyms in another municipality have
            // a different CityId and a different City text, so they are still excluded.
            return query.Where(x => x.CityId == cityId || x.City.ToLower() == normalizedCity);
        }

        if (cityId is not null)
        {
            return query.Where(x => x.CityId == cityId);
        }

        if (normalizedCity is not null)
        {
            return query.Where(x => x.City.ToLower() == normalizedCity);
        }

        // No city scope: an empty query instead of a national scan.
        return query.Where(_ => false);
    }

    private async Task<List<DeliveryNeighborhood>> LoadActiveCityNeighborhoodsAsync(
        Guid? cityId,
        string? cityName,
        CancellationToken cancellationToken)
    {
        var query = ApplyCityScope(
            _dbContext.DeliveryNeighborhoods.AsNoTracking().Where(x => x.IsActive),
            cityId,
            cityName);

        return await query
            .OrderBy(x => x.Neighborhood)
            .ToListAsync(cancellationToken);
    }

    private static DeliveryNeighborhoodResponseDto MapNeighborhood(
        DeliveryNeighborhood neighborhood,
        string? datasetVersion,
        double? distanceKm,
        double? appliedRadiusKm)
    {
        var isManual = string.Equals(neighborhood.Kind, GeographicProvenance.NeighborhoodKind.Manual, StringComparison.Ordinal)
            || neighborhood.Latitude is null
            || neighborhood.Longitude is null;

        bool? eligible = null;
        if (isManual)
        {
            // Manual neighborhoods are never distance-filtered; they require an explicit decision.
            eligible = false;
        }
        else if (distanceKm is not null && appliedRadiusKm is not null)
        {
            eligible = GeoDistance.IsWithinRadius(distanceKm.Value, appliedRadiusKm.Value);
        }

        return new DeliveryNeighborhoodResponseDto
        {
            Id = neighborhood.Id,
            Neighborhood = neighborhood.Neighborhood,
            NormalizedName = neighborhood.NormalizedName,
            City = neighborhood.City,
            CityId = neighborhood.CityId,
            OsmId = neighborhood.OsmId,
            OsmType = neighborhood.OsmType,
            PlaceType = neighborhood.PlaceType,
            Latitude = neighborhood.Latitude,
            Longitude = neighborhood.Longitude,
            Source = neighborhood.Source,
            IsActive = neighborhood.IsActive,
            Kind = neighborhood.Kind,
            CoordinateStatus = neighborhood.CoordinateStatus,
            CoordinateMethod = neighborhood.CoordinateMethod,
            CoordinatePointCount = neighborhood.CoordinatePointCount,
            DatasetVersion = datasetVersion,
            DistanceKm = distanceKm is null ? null : GeoDistance.RoundForDisplay(distanceKm.Value),
            Eligible = eligible,
            RequiresManualReview = isManual
        };
    }

    private async Task<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>> GetNeighborhoodsByCityAsync(string city, CancellationToken cancellationToken)
    {
        var neighborhoods = await _dbContext.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.IsActive && x.City.ToLower() == city.ToLower().Trim())
            .OrderBy(x => x.Neighborhood)
            .ToListAsync(cancellationToken);

        var datasetVersion = await GetActiveDatasetVersionAsync(null, cancellationToken);

        return neighborhoods
            .Select(x => MapNeighborhood(x, datasetVersion, distanceKm: null, appliedRadiusKm: null))
            .ToList();
    }

    /// <summary>
    /// Resolves the persisted <see cref="City"/> of the store address (normalized name + UF) to the
    /// canonical city identifier. Returns null when the client-supplied address text does not match
    /// a known city; callers then fall back to the textual name during the transition.
    /// </summary>
    private async Task<Guid?> ResolveCityIdAsync(StoreAddress? address, CancellationToken cancellationToken)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.City))
        {
            return null;
        }

        var normalizedName = NeighborhoodName.Normalize(address.City);
        if (normalizedName.Length == 0)
        {
            return null;
        }

        var uf = address.State?.Trim().ToUpperInvariant();

        var city = await _dbContext.Cities
            .AsNoTracking()
            .Where(x => x.NormalizedName == normalizedName && (uf == null || x.Uf == uf))
            .OrderBy(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return city?.Id;
    }

    /// <summary>
    /// Loads every active neighborhood of the store city keyed by canonical name so the write path
    /// can resolve <see cref="StoreDeliveryArea.NeighborhoodId"/> and its Haversine distance.
    /// </summary>
    private async Task<Dictionary<string, DeliveryNeighborhood>> LoadNeighborhoodLookupAsync(
        StoreAddress? address,
        Guid? cityId,
        CancellationToken cancellationToken)
    {
        var neighborhoods = await LoadActiveCityNeighborhoodsAsync(cityId, address?.City, cancellationToken);

        var lookup = new Dictionary<string, DeliveryNeighborhood>(neighborhoods.Count, StringComparer.Ordinal);
        foreach (var neighborhood in neighborhoods)
        {
            var key = neighborhood.NormalizedName.Length > 0
                ? neighborhood.NormalizedName
                : NeighborhoodName.Normalize(neighborhood.Neighborhood);

            if (key.Length == 0 || lookup.ContainsKey(key))
            {
                continue;
            }

            lookup[key] = neighborhood;
        }

        return lookup;
    }

    private async Task<string?> GetActiveDatasetVersionAsync(string? uf, CancellationToken cancellationToken)
    {
        var normalizedUf = uf?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedUf))
        {
            return null;
        }

        return await _dbContext.ImportDatasets
            .AsNoTracking()
            .Where(x => x.IsActive && x.Uf == normalizedUf)
            .OrderByDescending(x => x.ActivatedAtUtc)
            .Select(x => x.SourceVersion)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DeliveryNeighborhoodResponseDto?> CreateDeliveryNeighborhoodAsync(string neighborhood, string city, CancellationToken cancellationToken = default)
    {
        var displayName = neighborhood.Trim();
        var normalizedName = NeighborhoodName.Normalize(displayName);
        var cityName = city.Trim();

        if (normalizedName.Length == 0 || cityName.Length == 0)
            return null;

        var cityEntity = await _dbContext.Cities
            .AsNoTracking()
            .Where(x => x.NormalizedName == NeighborhoodName.Normalize(cityName))
            .OrderBy(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        // A manual neighborhood that collides with an official one in the same municipality must
        // reuse the existing row instead of creating a duplicate. The caller treats null as a
        // conflict, which is the existing contract.
        var existsQuery = _dbContext.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.IsActive && x.NormalizedName == normalizedName);

        existsQuery = cityEntity is not null
            ? existsQuery.Where(x => x.CityId == cityEntity.Id)
            : existsQuery.Where(x => x.City.ToLower() == cityName.ToLower());

        if (await existsQuery.AnyAsync(cancellationToken))
            return null;

        var dn = new DeliveryNeighborhood
        {
            CityId = cityEntity?.Id,
            Neighborhood = displayName,
            NormalizedName = normalizedName,
            City = cityEntity?.Name ?? cityName,
            Kind = GeographicProvenance.NeighborhoodKind.Manual,
            CoordinateStatus = GeographicProvenance.CoordinateStatus.Manual,
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
            IsActive = dn.IsActive,
            Kind = dn.Kind,
            CoordinateStatus = dn.CoordinateStatus,
            RequiresManualReview = true
        };
    }

    public async Task<CreateDeliveryNeighborhoodResultDto> CreateDeliveryNeighborhoodForOwnerAsync(
        Guid ownerUserId,
        string neighborhood,
        string city,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerUserId == ownerUserId, cancellationToken);

        if (store is null)
            return new CreateDeliveryNeighborhoodResultDto { NotFound = true };

        var storeAddress = await _dbContext.StoreAddresses
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == store.Id, cancellationToken);

        var storeCity = storeAddress?.City?.Trim();
        if (string.IsNullOrWhiteSpace(storeCity))
            return new CreateDeliveryNeighborhoodResultDto { CityRequired = true };

        // A seller may only create global neighborhoods for the city of their own store.
        // The client-supplied city is validated against the persisted store city and is
        // never trusted to select an arbitrary city; the persisted canonical city is used.
        if (!string.Equals(NormalizeText(city), NormalizeText(storeCity), StringComparison.Ordinal))
            return new CreateDeliveryNeighborhoodResultDto { Forbidden = true };

        var created = await CreateDeliveryNeighborhoodAsync(neighborhood, storeCity, cancellationToken);
        if (created is null)
            return new CreateDeliveryNeighborhoodResultDto { Conflict = true };

        return new CreateDeliveryNeighborhoodResultDto { Neighborhood = created };
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
            .Where(x => x.Name == BillingPlanSeeder.DefaultPlanName && x.IsActive)
            .SingleOrDefaultAsync(cancellationToken)
            ?? await _dbContext.Plans
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Amount)
                .ThenBy(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);

        var planId = plan?.Id;
        var planName = plan?.Name ?? BillingPlanSeeder.DefaultPlanName;
        var planAmount = plan?.Amount ?? BillingPlanSeeder.DefaultPlanAmount;
        var trialDays = plan?.TrialDays ?? BillingPlanSeeder.DefaultTrialDays;

        var now = DateTime.UtcNow;
        var nextBillingDateUtc = trialDays > 0
            ? now.AddDays(trialDays)
            : now.AddMonths(1);

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


