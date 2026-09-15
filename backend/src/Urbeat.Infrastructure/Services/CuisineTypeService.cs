using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class CuisineTypeService : ICuisineTypeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;

    public CuisineTypeService(ApplicationDbContext dbContext, IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<IReadOnlyCollection<CuisineTypeResponseDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CuisineTypes
            .AsNoTracking()
            .Where(x => x.IsActive && x.IsDefault && x.StoreId == null)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyCollection<CuisineTypeResponseDto>>(entities);
    }

    public async Task<IReadOnlyCollection<CuisineTypeResponseDto>> GetForStoreAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default)
    {
        var storeExists = await _dbContext.Stores
            .AsNoTracking()
            .AnyAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!storeExists)
        {
            return Array.Empty<CuisineTypeResponseDto>();
        }

        var entities = await _dbContext.CuisineTypes
            .AsNoTracking()
            .Where(x => x.IsActive && ((x.IsDefault && x.StoreId == null) || x.StoreId == storeId))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyCollection<CuisineTypeResponseDto>>(entities);
    }

    public async Task<CuisineTypeResponseDto?> CreateForStoreAsync(Guid ownerUserId, Guid storeId, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var storeExists = await _dbContext.Stores
            .AsNoTracking()
            .AnyAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!storeExists)
        {
            return null;
        }

        var trimmedName = name.Trim();
        var normalizedName = CuisineType.NormalizeName(trimmedName);

        // Nunca cria/muta categorias padrão protegidas.
        if (CuisineTypeDefaults.IsDefaultNormalizedName(normalizedName))
        {
            return null;
        }

        // Rejeita duplicado normalizado no escopo da loja.
        var duplicateInStore = await _dbContext.CuisineTypes
            .AnyAsync(x => x.StoreId == storeId && x.NormalizedName == normalizedName, cancellationToken);

        if (duplicateInStore)
        {
            return null;
        }

        // Defesa extra: nenhuma categoria global pode colidir com a chave normalizada.
        var collidesWithGlobal = await _dbContext.CuisineTypes
            .AnyAsync(x => x.StoreId == null && x.NormalizedName == normalizedName, cancellationToken);

        if (collidesWithGlobal)
        {
            return null;
        }

        var entity = new CuisineType
        {
            Name = trimmedName,
            IsActive = true,
            IsDefault = false,
            StoreId = storeId
        };

        _dbContext.CuisineTypes.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return _mapper.Map<CuisineTypeResponseDto>(entity);
    }

    public async Task<DeleteCuisineTypeResultDto> DeleteForStoreAsync(Guid ownerUserId, Guid storeId, Guid cuisineTypeId, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            return new DeleteCuisineTypeResultDto { NotFound = true };
        }

        if (store.OwnerUserId != ownerUserId)
        {
            return new DeleteCuisineTypeResultDto { Forbidden = true };
        }

        var category = await _dbContext.CuisineTypes
            .SingleOrDefaultAsync(x => x.Id == cuisineTypeId, cancellationToken);

        if (category is null)
        {
            return new DeleteCuisineTypeResultDto { NotFound = true };
        }

        // Categorias padrão protegidas nunca são excluídas por fluxos de loja.
        if (category.IsDefault)
        {
            return new DeleteCuisineTypeResultDto { Protected = true };
        }

        // Categorias privadas de outra loja não são sequer reveladas.
        if (category.StoreId != storeId)
        {
            return new DeleteCuisineTypeResultDto { NotFound = true };
        }

        var isReferenced = await _dbContext.Stores
            .AnyAsync(x => x.CuisineTypeId == cuisineTypeId, cancellationToken);

        if (isReferenced)
        {
            return new DeleteCuisineTypeResultDto { InUse = true };
        }

        _dbContext.CuisineTypes.Remove(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteCuisineTypeResultDto { Deleted = true };
    }
}
