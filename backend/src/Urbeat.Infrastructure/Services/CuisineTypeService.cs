using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Urbeat.Infrastructure.Services;

public sealed class CuisineTypeService : ICuisineTypeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IEfUnitOfWork _efUnitOfWork;

    public CuisineTypeService(ApplicationDbContext dbContext, IMapper mapper, IEfUnitOfWork efUnitOfWork)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _efUnitOfWork = efUnitOfWork;
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

    public async Task<CreateCuisineTypeResultDto> CreateForStoreAsync(Guid ownerUserId, Guid storeId, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new CreateCuisineTypeResultDto { Invalid = true };
        }

        var storeExists = await _dbContext.Stores
            .AsNoTracking()
            .AnyAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!storeExists)
        {
            return new CreateCuisineTypeResultDto { NotFound = true };
        }

        var trimmedName = name.Trim();
        var normalizedName = CuisineType.NormalizeName(trimmedName);

        // Nunca cria/muta categorias padrão protegidas.
        if (CuisineTypeDefaults.IsDefaultNormalizedName(normalizedName))
        {
            return new CreateCuisineTypeResultDto { Conflict = true };
        }

        // Rejeita duplicado normalizado no escopo da loja.
        var duplicateInStore = await _dbContext.CuisineTypes
            .AnyAsync(x => x.StoreId == storeId && x.NormalizedName == normalizedName, cancellationToken);

        if (duplicateInStore)
        {
            return new CreateCuisineTypeResultDto { Conflict = true };
        }

        // Defesa extra: nenhuma categoria global pode colidir com a chave normalizada.
        var collidesWithGlobal = await _dbContext.CuisineTypes
            .AnyAsync(x => x.StoreId == null && x.NormalizedName == normalizedName, cancellationToken);

        if (collidesWithGlobal)
        {
            return new CreateCuisineTypeResultDto { Conflict = true };
        }

        var entity = new CuisineType
        {
            Name = trimmedName,
            IsActive = true,
            IsDefault = false,
            StoreId = storeId
        };

        _dbContext.CuisineTypes.Add(entity);

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsStoreCuisineUniqueViolation(exception))
        {
            // Uma requisição concorrente persistiu o mesmo nome normalizado entre o pre-check e
            // este save; o índice único parcial é a garantia real. Reporta conflito em vez de 500.
            _dbContext.Entry(entity).State = EntityState.Detached;
            return new CreateCuisineTypeResultDto { Conflict = true };
        }

        return new CreateCuisineTypeResultDto
        {
            Created = true,
            CuisineType = _mapper.Map<CuisineTypeResponseDto>(entity)
        };
    }

    private static bool IsStoreCuisineUniqueViolation(DbUpdateException exception)
    {
        // PostgreSQL raises SQLSTATE 23505 when a concurrent request persists the same normalized
        // name for a store between the pre-check and SaveChanges. Only the store-scoped unique index
        // maps to a conflict; any other DbUpdateException propagates.
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } pg && (string.Equals(
            pg.ConstraintName,
            "IX_CuisineTypes_StoreId_NormalizedName",
            StringComparison.OrdinalIgnoreCase)
            || pg.MessageText.Contains("IX_CuisineTypes_StoreId_NormalizedName", StringComparison.OrdinalIgnoreCase));
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

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsStoreCuisineForeignKeyViolation(exception))
        {
            // Uma requisição concorrente associou esta categoria à loja (Stores.CuisineTypeId)
            // entre a verificação e o delete; a FK é a garantia real. Reporta "em uso" em vez de 500.
            _dbContext.Entry(category).State = EntityState.Unchanged;
            return new DeleteCuisineTypeResultDto { InUse = true };
        }

        return new DeleteCuisineTypeResultDto { Deleted = true };
    }

    private static bool IsStoreCuisineForeignKeyViolation(DbUpdateException exception)
    {
        // PostgreSQL raises SQLSTATE 23503 when a concurrent request points Stores.CuisineTypeId
        // at this category between the pre-check and the delete. Only that FK maps to "in use";
        // any other DbUpdateException propagates.
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation
        } pg && pg.MessageText.Contains("FK_Stores_CuisineTypes_CuisineTypeId", StringComparison.OrdinalIgnoreCase);
    }
}
