using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence;

public sealed class CuisineTypeSeeder
{
    private readonly ApplicationDbContext _dbContext;

    public CuisineTypeSeeder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await UpsertProtectedDefaultsAsync(cancellationToken);
        await HandleLegacyGlobalCategoriesAsync(cancellationToken);
    }

    private async Task UpsertProtectedDefaultsAsync(CancellationToken cancellationToken)
    {
        var globalCategories = await _dbContext.CuisineTypes
            .Where(x => x.StoreId == null)
            .ToListAsync(cancellationToken);

        var selected = new List<CuisineType>();

        foreach (var name in CuisineTypeDefaults.Names)
        {
            var normalizedName = CuisineType.NormalizeName(name);
            var existing = globalCategories.FirstOrDefault(x => x.NormalizedName == normalizedName);

            if (existing is null)
            {
                existing = new CuisineType
                {
                    Name = name,
                    IsActive = true,
                    IsDefault = true,
                    StoreId = null
                };
                _dbContext.CuisineTypes.Add(existing);
                globalCategories.Add(existing);
            }
            else
            {
                existing.IsActive = true;
                existing.StoreId = null;

                if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    existing.Name = name;
                }
            }

            selected.Add(existing);
        }

        // Only the 15 approved names may carry the protected flag. Legacy defaults outside the list
        // are demoted here and handled as legacy rows below.
        var selectedIds = selected.Select(x => x.Id).ToHashSet();
        foreach (var category in globalCategories)
        {
            category.IsDefault = selectedIds.Contains(category.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Legacy rule for global categories that are not one of the 15 approved defaults:
    // - referenced by a store -> copy once per referencing store and rewire, preserving the value;
    // - referenced by no store -> remove, so no unapproved global remains selectable.
    private async Task HandleLegacyGlobalCategoriesAsync(CancellationToken cancellationToken)
    {
        var approvedNormalizedNames = CuisineTypeDefaults.Names
            .Select(CuisineType.NormalizeName)
            .ToHashSet(StringComparer.Ordinal);

        var globalCategories = await _dbContext.CuisineTypes
            .Where(x => x.StoreId == null)
            .ToListAsync(cancellationToken);

        var legacyGlobals = globalCategories
            .Where(x => !approvedNormalizedNames.Contains(x.NormalizedName))
            .ToList();

        if (legacyGlobals.Count == 0)
        {
            return;
        }

        foreach (var legacy in legacyGlobals)
        {
            legacy.IsDefault = false;
        }

        var legacyIds = legacyGlobals.Select(x => x.Id).ToHashSet();
        var referencingStores = await _dbContext.Stores
            .Where(x => x.CuisineTypeId != null && legacyIds.Contains(x.CuisineTypeId.Value))
            .ToListAsync(cancellationToken);

        foreach (var store in referencingStores)
        {
            var legacy = legacyGlobals.First(x => x.Id == store.CuisineTypeId);
            var privateCopy = new CuisineType
            {
                Name = legacy.Name,
                IsActive = legacy.IsActive,
                IsDefault = false,
                StoreId = store.Id
            };

            _dbContext.CuisineTypes.Add(privateCopy);
            store.CuisineTypeId = privateCopy.Id;
        }

        _dbContext.CuisineTypes.RemoveRange(legacyGlobals);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
