using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class StoreAdditionalService : IStoreAdditionalService
{
    private readonly ApplicationDbContext _db;
    private readonly IEfUnitOfWork _unitOfWork;

    public StoreAdditionalService(ApplicationDbContext db, IEfUnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyCollection<StoreAdditionalDto>> ListAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default)
    {
        if (!await OwnsStoreAsync(ownerUserId, storeId, cancellationToken)) return [];

        await ImportLegacyAdditionalsAsync(storeId, cancellationToken);

        return await _db.StoreAdditionals.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new StoreAdditionalDto
            {
                Id = x.Id, StoreId = x.StoreId, GroupId = x.GroupId, GroupName = x.Group.Name,
                Name = x.Name, Description = x.Description, Price = x.Price, IsActive = x.IsActive,
                DisplayOrder = x.DisplayOrder, ProductCount = x.ProductAssignments.Count,
            }).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<StoreAdditionalGroupDto>> ListGroupsAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default)
    {
        if (!await OwnsStoreAsync(ownerUserId, storeId, cancellationToken)) return [];

        var names = await _db.Products.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .SelectMany(x => x.OptionGroups)
            .Select(x => x.Name.Trim())
            .Where(x => x != "")
            .Distinct()
            .ToListAsync(cancellationToken);
        var hasLegacyAdditionals = await _db.Set<ProductAdditional>().AsNoTracking()
            .AnyAsync(x => x.Product.StoreId == storeId && x.StoreAdditionalId == null, cancellationToken);
        if (hasLegacyAdditionals && names.All(x => !x.Equals("Extras", StringComparison.OrdinalIgnoreCase)))
            names.Add("Extras");

        var existing = await _db.StoreAdditionalGroups.Where(x => x.StoreId == storeId).ToListAsync(cancellationToken);
        foreach (var name in names.Where(name => existing.All(x => !x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
        {
            var group = new StoreAdditionalGroup { StoreId = storeId, Name = name, IsActive = true };
            _db.StoreAdditionalGroups.Add(group);
            existing.Add(group);
        }

        if (names.Count > 0) await _unitOfWork.SaveChangesAsync(cancellationToken);

        return existing.Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new StoreAdditionalGroupDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive })
            .ToList();
    }

    public async Task<(StoreAdditionalDto? Additional, bool NotFound, bool Forbidden)> CreateAsync(Guid ownerUserId, Guid storeId, StoreAdditionalRequestDto request, CancellationToken cancellationToken = default)
    {
        var ownership = await CheckOwnershipAsync(ownerUserId, storeId, cancellationToken);
        if (ownership is null) return (null, true, false);
        if (!ownership.Value) return (null, false, true);

        var group = await _db.StoreAdditionalGroups.SingleOrDefaultAsync(x => x.Id == request.GroupId && x.StoreId == storeId && x.IsActive, cancellationToken);
        if (group is null) return (null, true, false);
        var additional = new StoreAdditional { StoreId = storeId, GroupId = group.Id, Name = request.Name.Trim(), Description = request.Description?.Trim() ?? string.Empty, Price = request.Price, IsActive = request.IsActive, DisplayOrder = request.DisplayOrder };
        _db.StoreAdditionals.Add(additional);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return (await ToDtoAsync(additional.Id, cancellationToken), false, false);
    }

    public async Task<(StoreAdditionalDto? Additional, bool NotFound, bool Forbidden)> UpdateAsync(Guid ownerUserId, Guid storeId, Guid additionalId, StoreAdditionalRequestDto request, CancellationToken cancellationToken = default)
    {
        var additional = await _db.StoreAdditionals.Include(x => x.Group).SingleOrDefaultAsync(x => x.Id == additionalId && x.StoreId == storeId, cancellationToken);
        if (additional is null) return (null, true, false);
        if (!await OwnsStoreAsync(ownerUserId, storeId, cancellationToken)) return (null, false, true);
        var group = await _db.StoreAdditionalGroups.SingleOrDefaultAsync(x => x.Id == request.GroupId && x.StoreId == storeId && x.IsActive, cancellationToken);
        if (group is null) return (null, true, false);

        additional.GroupId = group.Id;
        additional.Name = request.Name.Trim();
        additional.Description = request.Description?.Trim() ?? string.Empty;
        additional.Price = request.Price;
        additional.IsActive = request.IsActive;
        additional.DisplayOrder = request.DisplayOrder;
        additional.MarkAsUpdated();
        await SyncSnapshotsAsync(additional, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return (await ToDtoAsync(additional.Id, cancellationToken), false, false);
    }

    public async Task<(StoreAdditionalDto? Additional, bool NotFound, bool Forbidden)> UpdateStatusAsync(Guid ownerUserId, Guid storeId, Guid additionalId, bool isActive, CancellationToken cancellationToken = default)
    {
        var additional = await _db.StoreAdditionals.SingleOrDefaultAsync(x => x.Id == additionalId && x.StoreId == storeId, cancellationToken);
        if (additional is null) return (null, true, false);
        if (!await OwnsStoreAsync(ownerUserId, storeId, cancellationToken)) return (null, false, true);
        additional.IsActive = isActive;
        additional.MarkAsUpdated();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return (await ToDtoAsync(additional.Id, cancellationToken), false, false);
    }

    public async Task<StoreAdditionalDeleteResult> DeleteAsync(Guid ownerUserId, Guid storeId, Guid additionalId, CancellationToken cancellationToken = default)
    {
        var additional = await _db.StoreAdditionals.SingleOrDefaultAsync(x => x.Id == additionalId && x.StoreId == storeId, cancellationToken);
        if (additional is null) return new StoreAdditionalDeleteResult { NotFound = true };
        if (!await OwnsStoreAsync(ownerUserId, storeId, cancellationToken)) return new StoreAdditionalDeleteResult { Forbidden = true };
        if (await _db.ProductAdditionalAssignments.AnyAsync(x => x.AdditionalId == additionalId, cancellationToken)) return new StoreAdditionalDeleteResult { HasProducts = true };
        _db.StoreAdditionals.Remove(additional);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new StoreAdditionalDeleteResult();
    }

    private async Task SyncSnapshotsAsync(StoreAdditional additional, CancellationToken cancellationToken)
    {
        await _db.Set<ProductAdditional>().Where(x => x.StoreAdditionalId == additional.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Name, additional.Name)
            .SetProperty(x => x.Price, additional.Price)
            .SetProperty(x => x.IsActive, additional.IsActive)
            .SetProperty(x => x.DisplayOrder, additional.DisplayOrder), cancellationToken);
    }

    private async Task ImportLegacyAdditionalsAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var legacy = await _db.Set<ProductAdditional>()
            .Include(x => x.Product)
            .Where(x => x.Product.StoreId == storeId && x.StoreAdditionalId == null)
            .ToListAsync(cancellationToken);
        if (legacy.Count == 0) return;

        var groups = await _db.StoreAdditionalGroups.Where(x => x.StoreId == storeId).ToListAsync(cancellationToken);
        var productGroupNames = await _db.Products.Where(x => x.StoreId == storeId)
            .SelectMany(x => x.OptionGroups).Select(x => new { x.ProductId, x.Name }).ToListAsync(cancellationToken);
        foreach (var item in legacy)
        {
            var groupName = productGroupNames.FirstOrDefault(x => x.ProductId == item.ProductId)?.Name.Trim();
            if (string.IsNullOrWhiteSpace(groupName)) groupName = "Extras";
            var group = groups.FirstOrDefault(x => x.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                group = new StoreAdditionalGroup { StoreId = storeId, Name = groupName };
                groups.Add(group);
                _db.StoreAdditionalGroups.Add(group);
            }

            var additional = await _db.StoreAdditionals.FirstOrDefaultAsync(x => x.StoreId == storeId && x.GroupId == group.Id && x.Name == item.Name, cancellationToken);
            if (additional is null)
            {
                additional = new StoreAdditional { StoreId = storeId, Group = group, Name = item.Name, Price = item.Price, IsActive = item.IsActive, DisplayOrder = item.DisplayOrder };
                _db.StoreAdditionals.Add(additional);
            }

            item.StoreAdditional = additional;
            item.StoreAdditionalId = additional.Id;
            _db.ProductAdditionalAssignments.Add(new ProductAdditionalAssignment { ProductId = item.ProductId, Additional = additional, AdditionalId = additional.Id });
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request may have imported the same legacy rows concurrently.
            _db.ChangeTracker.Clear();
        }
    }

    private async Task<StoreAdditionalDto?> ToDtoAsync(Guid id, CancellationToken cancellationToken) => await _db.StoreAdditionals.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new StoreAdditionalDto { Id = x.Id, StoreId = x.StoreId, GroupId = x.GroupId, GroupName = x.Group.Name, Name = x.Name, Description = x.Description, Price = x.Price, IsActive = x.IsActive, DisplayOrder = x.DisplayOrder, ProductCount = x.ProductAssignments.Count })
        .SingleOrDefaultAsync(cancellationToken);

    private Task<bool> OwnsStoreAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken) => _db.Stores.AnyAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

    private async Task<bool?> CheckOwnershipAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken)
    {
        var store = await _db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        return store is null ? null : store.OwnerUserId == ownerUserId;
    }
}
