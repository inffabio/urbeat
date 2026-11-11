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

        // Only the 20 approved names may carry the protected flag. Legacy defaults outside the list
        // are demoted here and handled as legacy rows below.
        var selectedIds = selected.Select(x => x.Id).ToHashSet();
        foreach (var category in globalCategories)
        {
            category.IsDefault = selectedIds.Contains(category.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Legacy rule for global categories that are not one of the 20 approved defaults:
    // - referenced by a store -> keep the exact saved value through a private category of that
    //   store and rewire; an existing private row with the same normalized key keeps its id and
    //   store link (the text is aligned to the legacy value, so no unique index is violated and
    //   no duplicate row is created);
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

        var referencingStoreIds = referencingStores.Select(x => x.Id).ToHashSet();
        var privateCategories = new List<CuisineType>();
        if (referencingStoreIds.Count > 0)
        {
            privateCategories = await _dbContext.CuisineTypes
                .Where(x => x.StoreId != null && referencingStoreIds.Contains(x.StoreId.Value))
                .ToListAsync(cancellationToken);
        }

        foreach (var store in referencingStores)
        {
            var legacy = legacyGlobals.First(x => x.Id == store.CuisineTypeId);

            // The scoped unique index is (StoreId, NormalizedName). Reusing a private row with the
            // same normalized key is the only way to keep the original text without failing the
            // index or duplicating the category; the name is aligned to the legacy value so the
            // store never loses what it had saved.
            var existingPrivate = privateCategories.FirstOrDefault(x =>
                x.StoreId == store.Id && x.NormalizedName == legacy.NormalizedName);

            if (existingPrivate is not null)
            {
                if (!string.Equals(existingPrivate.Name, legacy.Name, StringComparison.Ordinal))
                {
                    existingPrivate.Name = legacy.Name;
                }

                existingPrivate.IsDefault = false;
                store.CuisineTypeId = existingPrivate.Id;
                continue;
            }

            var privateCopy = new CuisineType
            {
                Name = legacy.Name,
                IsActive = legacy.IsActive,
                IsDefault = false,
                StoreId = store.Id
            };

            _dbContext.CuisineTypes.Add(privateCopy);
            privateCategories.Add(privateCopy);
            store.CuisineTypeId = privateCopy.Id;
        }

        _dbContext.CuisineTypes.RemoveRange(legacyGlobals);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
