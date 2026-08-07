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
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyCollection<CuisineTypeResponseDto>>(entities);
    }

    public async Task<CuisineTypeResponseDto> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        var existing = await _dbContext.CuisineTypes
            .FirstOrDefaultAsync(x => x.Name.ToLower() == normalized.ToLower(), cancellationToken);

        if (existing != null)
        {
            return _mapper.Map<CuisineTypeResponseDto>(existing);
        }

        var entity = new CuisineType
        {
            Name = normalized,
            IsActive = true
        };

        _dbContext.CuisineTypes.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return _mapper.Map<CuisineTypeResponseDto>(entity);
    }
}