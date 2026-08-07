using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class ProductService : IProductService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IImageUploadService _imageUploadService;

    public ProductService(ApplicationDbContext dbContext, IMapper mapper, IEfUnitOfWork efUnitOfWork, IImageUploadService imageUploadService)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _efUnitOfWork = efUnitOfWork;
        _imageUploadService = imageUploadService;
    }

    public async Task<IReadOnlyCollection<ProductResponseDto>> ListByStoreAsync(
        Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default)
    {
        var isOwner = await _dbContext.Stores
            .AnyAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!isOwner)
            return [];

        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Additionals)
            .Include(p => p.ChoiceOptions)
            .Include(p => p.Variations)
            .Include(p => p.WeightConfig)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Where(x => x.StoreId == storeId)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<ProductResponseDto>>(products);
        await EnrichWithCategoryNames(dtos, cancellationToken);
        dtos.Sort((a, b) =>
        {
            var catCompare = string.Compare(a.CategoryName, b.CategoryName, StringComparison.OrdinalIgnoreCase);
            return catCompare != 0 ? catCompare : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return dtos;
    }

    public async Task<UpdateProductResultDto> CreateAsync(
        Guid ownerUserId, Guid storeId, CreateProductRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores.SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        if (store is null)
            return new UpdateProductResultDto { NotFound = true };

        if (store.OwnerUserId != ownerUserId)
            return new UpdateProductResultDto { Forbidden = true };

        var categoryExists = await _dbContext.ProductCategories
            .AnyAsync(x => x.Id == request.CategoryId && x.StoreId == storeId, cancellationToken);

        if (!categoryExists)
            return new UpdateProductResultDto { NotFound = true };

        var catalogAdditionals = request.AdditionalIds is null
            ? []
            : await GetCatalogAdditionalsAsync(storeId, request.AdditionalIds, cancellationToken);
        if (request.AdditionalIds is not null && catalogAdditionals.Count != request.AdditionalIds.Distinct().Count())
            return new UpdateProductResultDto { NotFound = true };

        var product = new Product
        {
            StoreId = storeId,
            CategoryId = request.CategoryId,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            PromotionalPrice = request.PromotionalPrice,
            ImageUrl = request.ImageUrl?.Trim(),
            IsAvailable = request.IsAvailable,
            IsFeatured = request.IsFeatured,
            DisplayOrder = request.DisplayOrder,
            StockEnabled = request.StockEnabled,
            StockQuantity = request.StockQuantity,
            IsBestSeller = request.IsBestSeller,
            IsNew = request.IsNew,
            TagPriority = request.TagPriority,
            SaleMode = NormalizeSaleMode(request.SaleMode),
            Price = NormalizeBasePrice(request),
        };

        if (request.WeightConfig is { } wc)
            product.WeightConfig = BuildWeightConfig(wc);

        if (request.Variations != null)
        {
            foreach (var item in request.Variations)
                product.Variations.Add(BuildVariation(item));
        }
        EnsureDefaultVariation(product.Variations);

        if (request.AdditionalIds is not null)
        {
            AddCatalogAdditionals(product, catalogAdditionals);
        }
        else if (request.Additionals != null)
        {
            foreach (var item in request.Additionals)
                product.Additionals.Add(new ProductAdditional { Name = item.Name, Price = item.Price, IsActive = item.IsActive, IsRequired = item.IsRequired, DisplayOrder = item.DisplayOrder });
        }
        if (request.ChoiceOptions != null)
        {
            foreach (var item in request.ChoiceOptions)
                product.ChoiceOptions.Add(new ProductChoiceOption { Name = item.Name, Price = item.Price, IsActive = item.IsActive, IsRequired = item.IsRequired, DisplayOrder = item.DisplayOrder });
        }
        if (request.OptionGroups != null)
        {
            foreach (var group in request.OptionGroups)
                product.OptionGroups.Add(BuildOptionGroup(group, null));
        }

        await _dbContext.Products.AddAsync(product, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(ownerUserId, "ProductCreated", nameof(Product),
            product.Id, $"Product '{product.Name}' created.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<ProductResponseDto>(product);
        dto.CategoryName = await GetCategoryNameAsync(request.CategoryId, cancellationToken) ?? string.Empty;
        return new UpdateProductResultDto { Product = dto };
    }

    public async Task<UpdateProductResultDto> UpdateAsync(
        Guid ownerUserId, Guid productId, UpdateProductRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var isRelational = _dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";

        var product = isRelational
            ? await _dbContext.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, cancellationToken)
            : await _dbContext.Products
                .Include(p => p.Additionals)
                .Include(p => p.ChoiceOptions)
                .Include(p => p.Variations)
                .Include(p => p.WeightConfig)
                .Include(p => p.OptionGroups).ThenInclude(g => g.Items)
                .SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);

        if (product is null)
            return new UpdateProductResultDto { NotFound = true };

        var isOwner = await _dbContext.Stores
            .AnyAsync(x => x.Id == product.StoreId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!isOwner)
            return new UpdateProductResultDto { Forbidden = true };

        var categoryExists = await _dbContext.ProductCategories
            .AnyAsync(x => x.Id == request.CategoryId && x.StoreId == product.StoreId, cancellationToken);

        if (!categoryExists)
            return new UpdateProductResultDto { NotFound = true };

        var catalogAdditionals = request.AdditionalIds is null
            ? []
            : await GetCatalogAdditionalsAsync(product.StoreId, request.AdditionalIds, cancellationToken);
        if (request.AdditionalIds is not null && catalogAdditionals.Count != request.AdditionalIds.Distinct().Count())
            return new UpdateProductResultDto { NotFound = true };

        var oldImageUrl = product.ImageUrl;
        var newImageUrl = request.ImageUrl?.Trim();
        var saleMode = NormalizeSaleMode(request.SaleMode);
        var basePrice = NormalizeBasePrice(saleMode, request.Price, request.Variations, request.WeightConfig);

        if (isRelational)
        {
            await _dbContext.Products
                .Where(p => p.Id == productId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.CategoryId, request.CategoryId)
                    .SetProperty(p => p.Name, request.Name.Trim())
                    .SetProperty(p => p.Description, request.Description.Trim())
                    .SetProperty(p => p.Price, basePrice)
                    .SetProperty(p => p.PromotionalPrice, request.PromotionalPrice)
                    .SetProperty(p => p.ImageUrl, newImageUrl)
                    .SetProperty(p => p.IsAvailable, request.IsAvailable)
                    .SetProperty(p => p.IsFeatured, request.IsFeatured)
                    .SetProperty(p => p.DisplayOrder, request.DisplayOrder)
                    .SetProperty(p => p.StockEnabled, request.StockEnabled)
                    .SetProperty(p => p.StockQuantity, request.StockQuantity)
                    .SetProperty(p => p.IsBestSeller, request.IsBestSeller)
                    .SetProperty(p => p.IsNew, request.IsNew)
                    .SetProperty(p => p.TagPriority, request.TagPriority)
                    .SetProperty(p => p.SaleMode, saleMode)
                    .SetProperty(p => p.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(oldImageUrl) && oldImageUrl != newImageUrl)
            {
                try { await _imageUploadService.DeleteAsync(oldImageUrl, cancellationToken); } catch { }
            }

            if (request.AdditionalIds is not null)
            {
                await _dbContext.Set<ProductAdditionalAssignment>().Where(a => a.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
                await _dbContext.Set<ProductAdditional>().Where(a => a.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
                foreach (var additional in catalogAdditionals)
                {
                    _dbContext.Set<ProductAdditionalAssignment>().Add(new ProductAdditionalAssignment { ProductId = productId, AdditionalId = additional.Id });
                    _dbContext.Set<ProductAdditional>().Add(BuildCatalogSnapshot(productId, additional));
                }
            }
            else
            {
                await _dbContext.Set<ProductAdditionalAssignment>().Where(a => a.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
                await _dbContext.Set<ProductAdditional>().Where(a => a.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
            }
            if (request.AdditionalIds is null && request.Additionals != null)
                foreach (var item in request.Additionals)
                    _dbContext.Set<ProductAdditional>().Add(new ProductAdditional { ProductId = productId, Name = item.Name, Price = item.Price, IsActive = item.IsActive, IsRequired = item.IsRequired, DisplayOrder = item.DisplayOrder });

            await _dbContext.Set<ProductChoiceOption>().Where(c => c.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
            if (request.ChoiceOptions != null)
                foreach (var item in request.ChoiceOptions)
                    _dbContext.Set<ProductChoiceOption>().Add(new ProductChoiceOption { ProductId = productId, Name = item.Name, Price = item.Price, IsActive = item.IsActive, IsRequired = item.IsRequired, DisplayOrder = item.DisplayOrder });

            await _dbContext.Set<ProductVariation>().Where(v => v.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
            if (request.Variations != null)
            {
                var newVariations = request.Variations.Select(BuildVariation).ToList();
                EnsureDefaultVariation(newVariations);
                foreach (var variation in newVariations)
                {
                    variation.ProductId = productId;
                    _dbContext.Set<ProductVariation>().Add(variation);
                }
            }

            await _dbContext.Set<ProductWeightConfig>().Where(w => w.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
            if (saleMode == "variable_weight" && request.WeightConfig is { } weightConfig)
            {
                var config = BuildWeightConfig(weightConfig);
                config.ProductId = productId;
                _dbContext.Set<ProductWeightConfig>().Add(config);
            }

            await _dbContext.ProductOptionItems.Where(i => i.Group.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
            await _dbContext.ProductOptionGroups.Where(g => g.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
            if (request.OptionGroups != null)
            {
                foreach (var group in request.OptionGroups)
                    _dbContext.ProductOptionGroups.Add(BuildOptionGroup(group, productId));
            }
        }
        else
        {
            product.CategoryId = request.CategoryId;
            product.Name = request.Name.Trim();
            product.Description = request.Description.Trim();
            product.Price = basePrice;
            product.PromotionalPrice = request.PromotionalPrice;
            product.ImageUrl = newImageUrl;
            product.IsAvailable = request.IsAvailable;
            product.IsFeatured = request.IsFeatured;
            product.DisplayOrder = request.DisplayOrder;
            product.StockEnabled = request.StockEnabled;
            product.StockQuantity = request.StockQuantity;
            product.IsBestSeller = request.IsBestSeller;
            product.IsNew = request.IsNew;
            product.TagPriority = request.TagPriority;
            product.SaleMode = saleMode;
            product.MarkAsUpdated();

            _dbContext.RemoveRange(product.Additionals);
            _dbContext.RemoveRange(_dbContext.Set<ProductAdditionalAssignment>().Where(a => a.ProductId == productId).ToList());
            if (request.AdditionalIds is not null)
            {
                foreach (var additional in catalogAdditionals)
                {
                    product.AdditionalAssignments.Add(new ProductAdditionalAssignment { ProductId = productId, AdditionalId = additional.Id });
                    product.Additionals.Add(BuildCatalogSnapshot(productId, additional));
                }
            }
            else if (request.Additionals != null)
                foreach (var item in request.Additionals)
                    product.Additionals.Add(new ProductAdditional { Name = item.Name, Price = item.Price, IsActive = item.IsActive, IsRequired = item.IsRequired, DisplayOrder = item.DisplayOrder });

            _dbContext.RemoveRange(product.ChoiceOptions);
            if (request.ChoiceOptions != null)
                foreach (var item in request.ChoiceOptions)
                    product.ChoiceOptions.Add(new ProductChoiceOption { Name = item.Name, Price = item.Price, IsActive = item.IsActive, IsRequired = item.IsRequired, DisplayOrder = item.DisplayOrder });

            _dbContext.RemoveRange(product.Variations);
            if (request.Variations != null)
            {
                var newVariations = request.Variations.Select(BuildVariation).ToList();
                EnsureDefaultVariation(newVariations);
                foreach (var variation in newVariations)
                    product.Variations.Add(variation);
            }

            if (product.WeightConfig is not null)
                _dbContext.Remove(product.WeightConfig);
            if (saleMode == "variable_weight" && request.WeightConfig is { } wc2)
                product.WeightConfig = BuildWeightConfig(wc2);

            _dbContext.RemoveRange(product.OptionGroups.SelectMany(g => g.Items));
            _dbContext.RemoveRange(product.OptionGroups);
            if (request.OptionGroups != null)
            {
                foreach (var group in request.OptionGroups)
                    product.OptionGroups.Add(BuildOptionGroup(group, productId));
            }
        }

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        if (!isRelational && !string.IsNullOrWhiteSpace(oldImageUrl) && oldImageUrl != newImageUrl)
        {
            try { await _imageUploadService.DeleteAsync(oldImageUrl, cancellationToken); } catch { }
        }

        await WriteAuditLogAsync(ownerUserId, "ProductUpdated", nameof(Product),
            productId, $"Product '{(request.Name ?? product.Name).Trim()}' updated.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var responseProduct = isRelational
            ? await _dbContext.Products.AsNoTracking().Include(p => p.Additionals).Include(p => p.ChoiceOptions).Include(p => p.Variations).Include(p => p.WeightConfig).Include(p => p.OptionGroups).ThenInclude(g => g.Items).SingleAsync(x => x.Id == productId, cancellationToken)
            : product;

        var dto = _mapper.Map<ProductResponseDto>(responseProduct);
        dto.CategoryName = await GetCategoryNameAsync(request.CategoryId, cancellationToken) ?? string.Empty;
        return new UpdateProductResultDto { Product = dto };
    }

    public async Task<UpdateProductResultDto> UpdateAvailabilityAsync(
        Guid ownerUserId, Guid productId, bool isAvailable,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products.SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
        if (product is null)
            return new UpdateProductResultDto { NotFound = true };

        var isOwner = await _dbContext.Stores
            .AnyAsync(x => x.Id == product.StoreId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!isOwner)
            return new UpdateProductResultDto { Forbidden = true };

        product.IsAvailable = isAvailable;
        product.MarkAsUpdated();

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(ownerUserId, "ProductAvailabilityUpdated", nameof(Product),
            product.Id, $"Product '{product.Name}' availability set to {isAvailable}.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<ProductResponseDto>(product);
        dto.CategoryName = await GetCategoryNameAsync(product.CategoryId, cancellationToken) ?? string.Empty;
        return new UpdateProductResultDto { Product = dto };
    }

    public async Task<UpdateProductResultDto> UpdateImageAsync(
        Guid ownerUserId, Guid productId, string imageUrl,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products.SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
        if (product is null)
            return new UpdateProductResultDto { NotFound = true };

        var isOwner = await _dbContext.Stores
            .AnyAsync(x => x.Id == product.StoreId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!isOwner)
            return new UpdateProductResultDto { Forbidden = true };

        var oldImage = product.ImageUrl;
        product.ImageUrl = imageUrl;
        product.MarkAsUpdated();

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(oldImage) && oldImage != imageUrl)
        {
            try { await _imageUploadService.DeleteAsync(oldImage, cancellationToken); } catch { }
        }

        await WriteAuditLogAsync(ownerUserId, "ProductImageUpdated", nameof(Product),
            product.Id, $"Product '{product.Name}' image updated.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<ProductResponseDto>(product);
        dto.CategoryName = await GetCategoryNameAsync(product.CategoryId, cancellationToken) ?? string.Empty;
        return new UpdateProductResultDto { Product = dto };
    }

    public async Task<bool> DeleteAsync(
        Guid ownerUserId, Guid productId,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products.SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
        if (product is null)
            return false;

        var isOwner = await _dbContext.Stores
            .AnyAsync(x => x.Id == product.StoreId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!isOwner)
            return false;

        if (!string.IsNullOrWhiteSpace(product.ImageUrl))
        {
            try { await _imageUploadService.DeleteAsync(product.ImageUrl, cancellationToken); }
            catch { /* best-effort */ }
        }

        _dbContext.Products.Remove(product);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(ownerUserId, "ProductDeleted", nameof(Product),
            productId, $"Product '{product.Name}' deleted.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyCollection<ProductResponseDto>> BatchUpsertAsync(
        Guid ownerUserId, Guid storeId, BatchUpsertProductsRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores.SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        if (store is null || store.OwnerUserId != ownerUserId)
            return [];

        var existingProducts = await _dbContext.Products
            .Include(p => p.Additionals)
            .Include(p => p.ChoiceOptions)
            .Include(p => p.Variations)
            .Where(x => x.StoreId == storeId)
            .ToListAsync(cancellationToken);

        foreach (var p in existingProducts)
        {
            if (!string.IsNullOrWhiteSpace(p.ImageUrl))
            {
                try { await _imageUploadService.DeleteAsync(p.ImageUrl, cancellationToken); }
                catch { /* best-effort */ }
            }
        }

        _dbContext.Products.RemoveRange(existingProducts);

        var created = new List<Product>();
        var order = 0;
        foreach (var item in request.Items)
        {
            var product = new Product
            {
                StoreId = storeId,
                CategoryId = item.CategoryId,
                Name = item.Name.Trim(),
                Description = (item.Description ?? string.Empty).Trim(),
                Price = item.Price,
                ImageUrl = item.ImageUrl?.Trim(),
                IsAvailable = item.IsAvailable,
                DisplayOrder = item.DisplayOrder > 0 ? item.DisplayOrder : order
            };
            _dbContext.Products.Add(product);
            created.Add(product);
            order++;
        }

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(ownerUserId, "ProductsBatchUpserted", nameof(Product),
            storeId, $"{created.Count} products batch upserted.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var dtos = _mapper.Map<List<ProductResponseDto>>(created);
        await EnrichWithCategoryNames(dtos, cancellationToken);
        return dtos;
    }

    private async Task EnrichWithCategoryNames(List<ProductResponseDto> dtos, CancellationToken cancellationToken)
    {
        var categoryIds = dtos.Select(x => x.CategoryId).Distinct().ToList();
        var names = await _dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        for (var i = 0; i < dtos.Count; i++)
        {
            dtos[i].CategoryName = names.GetValueOrDefault(dtos[i].CategoryId) ?? string.Empty;
        }
    }

    private async Task<string?> GetCategoryNameAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        return await _dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => x.Id == categoryId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Constrói um grupo de opções normalizando o tipo de escolha,
    /// os limites min/máx e a obrigatoriedade (fonte da verdade no servidor).
    /// </summary>
    private static ProductOptionGroup BuildOptionGroup(ProductOptionGroupDto group, Guid? productId)
    {
        var choiceType = NormalizeChoiceType(group.ChoiceType);

        // Escolha única sempre tem máximo 1.
        var max = choiceType == "single" ? 1 : Math.Max(1, group.MaxChoices);
        var min = Math.Clamp(group.MinChoices, 0, max);

        var g = new ProductOptionGroup
        {
            Name = group.Name?.Trim() ?? string.Empty,
            ChoiceType = choiceType,
            MinChoices = min,
            MaxChoices = max,
            IsRequired = min >= 1,
            DisplayOrder = group.DisplayOrder,
        };

        if (productId.HasValue)
            g.ProductId = productId.Value;

        if (group.Items != null)
        {
            foreach (var item in group.Items)
                g.Items.Add(new ProductOptionItem { Name = item.Name?.Trim() ?? string.Empty, Price = item.Price, DisplayOrder = item.DisplayOrder });
        }

        return g;
    }

    private static string NormalizeChoiceType(string? type) => (type?.Trim().ToLowerInvariant()) switch
    {
        "single" => "single",
        _ => "multiple",
    };

    private static string NormalizeSaleMode(string? mode) => (mode?.Trim().ToLowerInvariant()) switch
    {
        "size" => "size",
        "fixed_weight" => "fixed_weight",
        "variable_weight" => "variable_weight",
        _ => "single",
    };

    private static ProductVariation BuildVariation(ProductVariationDto item)
    {
        var weightGrams = item.WeightGrams is > 0 ? item.WeightGrams : null;
        var name = string.IsNullOrWhiteSpace(item.Name) && weightGrams is int g
            ? FormatWeightLabel(g)
            : item.Name?.Trim() ?? string.Empty;

        return new ProductVariation
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
            WeightGrams = weightGrams,
            Price = item.Price,
            PromotionalPrice = item.PromotionalPrice,
            IsDefault = item.IsDefault,
            IsActive = item.IsActive,
            IsRequired = item.IsRequired,
            DisplayOrder = item.DisplayOrder,
        };
    }

    /// <summary>Garante exatamente uma variação padrão entre as ativas.</summary>
    private static void EnsureDefaultVariation(ICollection<ProductVariation> variations)
    {
        if (variations.Count == 0)
            return;

        var defaults = variations.Where(v => v.IsDefault && v.IsActive).ToList();
        if (defaults.Count == 1)
            return;

        foreach (var v in variations)
            v.IsDefault = false;

        var chosen = defaults.FirstOrDefault()
            ?? variations.FirstOrDefault(v => v.IsActive)
            ?? variations.First();
        chosen.IsDefault = true;
    }

    private static ProductWeightConfig BuildWeightConfig(ProductWeightConfigRequestDto config)
    {
        return new ProductWeightConfig
        {
            PricePerKg = config.PricePerKg,
            MinGrams = config.MinGrams,
            MaxGrams = config.MaxGrams,
            IncrementGrams = config.IncrementGrams,
            IsEstimated = config.IsEstimated,
        };
    }

    private async Task<List<StoreAdditional>> GetCatalogAdditionalsAsync(Guid storeId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var distinctIds = ids.Distinct().ToArray();
        return await _dbContext.StoreAdditionals
            .Where(x => x.StoreId == storeId && distinctIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
    }

    private static void AddCatalogAdditionals(Product product, IReadOnlyCollection<StoreAdditional> additionals)
    {
        foreach (var additional in additionals)
        {
            product.AdditionalAssignments.Add(new ProductAdditionalAssignment { ProductId = product.Id, AdditionalId = additional.Id });
            product.Additionals.Add(BuildCatalogSnapshot(product.Id, additional));
        }
    }

    private static ProductAdditional BuildCatalogSnapshot(Guid productId, StoreAdditional additional) => new()
    {
        ProductId = productId,
        StoreAdditionalId = additional.Id,
        Name = additional.Name,
        Price = additional.Price,
        IsActive = additional.IsActive,
        DisplayOrder = additional.DisplayOrder,
    };

    private decimal NormalizeBasePrice(CreateProductRequestDto request)
        => NormalizeBasePrice(NormalizeSaleMode(request.SaleMode), request.Price, request.Variations, request.WeightConfig);

    /// <summary>
    /// Preço base normalizado no servidor: para tamanho/peso fixo usa a menor variação ativa
    /// ("A partir de"); para peso variável usa o preço do peso mínimo.
    /// </summary>
    private static decimal NormalizeBasePrice(
        string saleMode,
        decimal price,
        IReadOnlyCollection<ProductVariationDto>? variations,
        ProductWeightConfigRequestDto? weightConfig)
    {
        switch (saleMode)
        {
            case "size":
            case "fixed_weight":
                var active = variations?.Where(v => v.IsActive && v.Price > 0).ToList();
                return active is { Count: > 0 } ? active.Min(v => v.Price) : price;
            case "variable_weight":
                return weightConfig is not null
                    ? Math.Round(weightConfig.PricePerKg * weightConfig.MinGrams / 1000m, 2)
                    : price;
            default:
                return price;
        }
    }

    private static string FormatWeightLabel(int grams)
        => grams >= 1000 ? $"{(grams / 1000m):0.##} kg" : $"{grams} g";

    private async Task WriteAuditLogAsync(
        Guid userId, string auditEvent, string entity, Guid entityId,
        string description, string? ipAddress, CancellationToken cancellationToken)
    {
        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = userId,
            Event = auditEvent,
            Entity = entity,
            EntityId = entityId,
            Description = description,
            IpAddress = ipAddress,
        }, cancellationToken);
    }
}
