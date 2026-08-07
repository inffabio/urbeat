using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence.ReadRepositories;

public sealed class ProductReadRepository : IProductReadRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;

    public ProductReadRepository(ApplicationDbContext dbContext, IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<IReadOnlyCollection<ProductCategoryResponseDto>> ListCategoriesByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default)
    {
        var categories = await _dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsActive)
            .Where(x => _dbContext.Products.Any(p =>
                p.StoreId == storeId &&
                p.CategoryId == x.Id &&
                p.IsAvailable))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<ProductCategoryResponseDto>>(categories);
    }

    public async Task<IReadOnlyCollection<ProductResponseDto>> ListAvailableProductsByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default)
    {
        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Additionals)
            .Include(p => p.ChoiceOptions)
            .Include(p => p.Variations)
            .Include(p => p.WeightConfig)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Where(x => x.StoreId == storeId && x.IsAvailable)
            .Where(x => _dbContext.ProductCategories.Any(c =>
                c.Id == x.CategoryId &&
                c.StoreId == storeId &&
                c.IsActive))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return await MapAndEnrichAsync(products, storeId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ProductResponseDto>> ListFeaturedProductsByStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default)
    {
        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Additionals)
            .Include(p => p.ChoiceOptions)
            .Include(p => p.Variations)
            .Include(p => p.WeightConfig)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Where(x => x.StoreId == storeId && x.IsAvailable && x.IsFeatured)
            .Where(x => _dbContext.ProductCategories.Any(c =>
                c.Id == x.CategoryId &&
                c.StoreId == storeId &&
                c.IsActive))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return await MapAndEnrichAsync(products, storeId, cancellationToken);
    }

    private async Task<IReadOnlyCollection<ProductResponseDto>> MapAndEnrichAsync(
        List<Product> products, Guid storeId, CancellationToken cancellationToken)
    {
        foreach (var product in products)
            product.Additionals = product.Additionals.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToList();

        var dtos = _mapper.Map<List<ProductResponseDto>>(products);

        var categoryNames = await _dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        for (var i = 0; i < dtos.Count; i++)
        {
            dtos[i].CategoryName = categoryNames.GetValueOrDefault(dtos[i].CategoryId) ?? string.Empty;
        }

        return dtos;
    }
}
