using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class ProductCategoryService : IProductCategoryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IEfUnitOfWork _efUnitOfWork;

    public ProductCategoryService(ApplicationDbContext dbContext, IMapper mapper, IEfUnitOfWork efUnitOfWork)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _efUnitOfWork = efUnitOfWork;
    }

    public async Task<IReadOnlyCollection<ProductCategoryResponseDto>> ListByStoreAsync(
        Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default)
    {
        var isOwner = await _dbContext.Stores
            .AnyAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!isOwner)
            return [];

        var categories = await _dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<ProductCategoryResponseDto>>(categories);
    }

    public async Task<UpsertProductCategoryResultDto> CreateAsync(
        Guid ownerUserId, Guid storeId, CreateProductCategoryRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores.SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        if (store is null)
            return new UpsertProductCategoryResultDto { NotFound = true };

        if (store.OwnerUserId != ownerUserId)
            return new UpsertProductCategoryResultDto { Forbidden = true };

        var trimmedName = request.Name.Trim();

        var existing = await _dbContext.ProductCategories
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Name == trimmedName, cancellationToken);

        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                existing.MarkAsUpdated();
                await _efUnitOfWork.SaveChangesAsync(cancellationToken);

                return new UpsertProductCategoryResultDto
                {
                    Category = _mapper.Map<ProductCategoryResponseDto>(existing),
                };
            }

            return new UpsertProductCategoryResultDto { Conflict = true };
        }

        var maxOrder = await _dbContext.ProductCategories
            .Where(x => x.StoreId == storeId)
            .MaxAsync(x => (int?)x.DisplayOrder, cancellationToken) ?? 0;

        var category = new ProductCategory
        {
            StoreId = storeId,
            Name = trimmedName,
            Description = request.Description?.Trim(),
            DisplayOrder = maxOrder + 1,
            IsActive = request.IsActive,
            IsFeatured = request.IsFeatured,
        };

        await _dbContext.ProductCategories.AddAsync(category, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(ownerUserId, "ProductCategoryCreated", nameof(ProductCategory),
            category.Id, $"Category '{category.Name}' created.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpsertProductCategoryResultDto
        {
            Category = _mapper.Map<ProductCategoryResponseDto>(category),
        };
    }

    public async Task<UpsertProductCategoryResultDto> UpdateAsync(
        Guid ownerUserId, Guid categoryId, UpdateProductCategoryRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.ProductCategories
            .SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

        if (category is null)
            return new UpsertProductCategoryResultDto { NotFound = true };

        var isOwner = await _dbContext.Stores
            .AnyAsync(x => x.Id == category.StoreId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!isOwner)
            return new UpsertProductCategoryResultDto { Forbidden = true };

        var duplicate = await _dbContext.ProductCategories.AnyAsync(x => x.StoreId == category.StoreId && x.Id != categoryId && x.Name == request.Name.Trim(), cancellationToken);
        if (duplicate)
            return new UpsertProductCategoryResultDto { Conflict = true };

        category.Name = request.Name.Trim();
        category.Description = request.Description?.Trim();
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;
        category.IsFeatured = request.IsFeatured;
        category.MarkAsUpdated();

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(ownerUserId, "ProductCategoryUpdated", nameof(ProductCategory),
            category.Id, $"Category '{category.Name}' updated.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpsertProductCategoryResultDto
        {
            Category = _mapper.Map<ProductCategoryResponseDto>(category),
        };
    }

    public async Task<ProductCategoryDeleteResult> DeleteAsync(
        Guid ownerUserId, Guid categoryId,
        Guid? reassignCategoryId = null,
        string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.ProductCategories
            .SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

        if (category is null)
            return ProductCategoryDeleteResult.NotFound;

        var isOwner = await _dbContext.Stores
            .AnyAsync(x => x.Id == category.StoreId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!isOwner)
            return ProductCategoryDeleteResult.Forbidden;

        if (reassignCategoryId.HasValue && reassignCategoryId.Value != Guid.Empty)
        {
            var targetExists = await _dbContext.ProductCategories
                .AnyAsync(x => x.Id == reassignCategoryId.Value && x.StoreId == category.StoreId, cancellationToken);

            if (targetExists)
            {
                await _dbContext.Products
                    .Where(x => x.StoreId == category.StoreId && x.CategoryId == categoryId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, reassignCategoryId.Value), cancellationToken);
            }
        }

        var hasProducts = await _dbContext.Products
            .AnyAsync(x => x.StoreId == category.StoreId && x.CategoryId == categoryId, cancellationToken);

        if (hasProducts)
            return ProductCategoryDeleteResult.HasProducts;

        _dbContext.ProductCategories.Remove(category);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(ownerUserId, "ProductCategoryDeleted", nameof(ProductCategory),
            categoryId, $"Category '{category.Name}' deleted.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return ProductCategoryDeleteResult.Deleted;
    }

    public async Task<ReorderStoreCategoriesResult> ReorderAsync(
        Guid ownerUserId, Guid storeId,
        ReorderStoreCategoriesRequestDto items,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        if (store is null)
            return new ReorderStoreCategoriesResult { NotFound = true };
        if (store.OwnerUserId != ownerUserId)
            return new ReorderStoreCategoriesResult { Forbidden = true };

        var categories = await _dbContext.ProductCategories
            .Where(x => x.StoreId == storeId)
            .ToListAsync(cancellationToken);
        var expectedIds = categories.Select(x => x.Id).ToHashSet();
        var receivedIds = items.Select(x => x.Id).ToHashSet();
        var receivedOrders = items.Select(x => x.DisplayOrder).ToArray();
        if (items.Count != categories.Count || receivedIds.Count != categories.Count || !receivedIds.SetEquals(expectedIds) || receivedOrders.Distinct().Count() != categories.Count || receivedOrders.Any(x => x < 1 || x > categories.Count))
            return new ReorderStoreCategoriesResult { Invalid = true };

        foreach (var category in categories)
        {
            var item = items.FirstOrDefault(x => x.Id == category.Id);
            if (item is not null)
            {
                category.DisplayOrder = item.DisplayOrder;
                category.MarkAsUpdated();
            }
        }

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(ownerUserId, "ProductCategoryReordered", nameof(ProductCategory),
            storeId, $"Categories reordered for store.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return new ReorderStoreCategoriesResult();
    }

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
