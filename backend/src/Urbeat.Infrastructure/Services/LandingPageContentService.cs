using Urbeat.Application.Dtos;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class LandingPageContentService : ILandingPageContentService
{
    private readonly ApplicationDbContext _context;

    public LandingPageContentService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<LandingPageContentResponseDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _context.LandingPageContents
            .Where(x => x.IsActive)
            .OrderBy(x => x.Section)
            .ThenBy(x => x.DisplayOrder)
            .Select(x => new LandingPageContentResponseDto
            {
                Id = x.Id,
                Section = x.Section,
                Key = x.Key,
                Value = x.Value,
                DisplayOrder = x.DisplayOrder,
                IsActive = x.IsActive,
                Description = x.Description,
                CreatedAt = x.CreatedAtUtc,
                UpdatedAt = x.UpdatedAtUtc ?? x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<LandingPageContentResponseDto>> GetBySectionAsync(string section, CancellationToken cancellationToken)
    {
        return await _context.LandingPageContents
            .Where(x => x.Section == section && x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new LandingPageContentResponseDto
            {
                Id = x.Id,
                Section = x.Section,
                Key = x.Key,
                Value = x.Value,
                DisplayOrder = x.DisplayOrder,
                IsActive = x.IsActive,
                Description = x.Description,
                CreatedAt = x.CreatedAtUtc,
                UpdatedAt = x.UpdatedAtUtc ?? x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<LandingPageContentResponseDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.LandingPageContents
            .Where(x => x.Id == id)
            .Select(x => new LandingPageContentResponseDto
            {
                Id = x.Id,
                Section = x.Section,
                Key = x.Key,
                Value = x.Value,
                DisplayOrder = x.DisplayOrder,
                IsActive = x.IsActive,
                Description = x.Description,
                CreatedAt = x.CreatedAtUtc,
                UpdatedAt = x.UpdatedAtUtc ?? x.CreatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<LandingPageContentResponseDto> CreateAsync(LandingPageContentRequestDto request, CancellationToken cancellationToken)
    {
        var entity = new LandingPageContent
        {
            Section = request.Section,
            Key = request.Key,
            Value = request.Value,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            Description = request.Description
        };

        _context.LandingPageContents.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return new LandingPageContentResponseDto
        {
            Id = entity.Id,
            Section = entity.Section,
            Key = entity.Key,
            Value = entity.Value,
            DisplayOrder = entity.DisplayOrder,
            IsActive = entity.IsActive,
            Description = entity.Description,
            CreatedAt = entity.CreatedAtUtc,
            UpdatedAt = entity.UpdatedAtUtc ?? entity.CreatedAtUtc
        };
    }

    public async Task<LandingPageContentResponseDto> UpdateAsync(Guid id, LandingPageContentRequestDto request, CancellationToken cancellationToken)
    {
        var entity = await _context.LandingPageContents.FindAsync(new object[] { id }, cancellationToken);
        if (entity == null)
        {
            throw new KeyNotFoundException("Landing page content not found.");
        }

        entity.Section = request.Section;
        entity.Key = request.Key;
        entity.Value = request.Value;
        entity.DisplayOrder = request.DisplayOrder;
        entity.IsActive = request.IsActive;
        entity.Description = request.Description;
        entity.MarkAsUpdated();

        await _context.SaveChangesAsync(cancellationToken);

        return new LandingPageContentResponseDto
        {
            Id = entity.Id,
            Section = entity.Section,
            Key = entity.Key,
            Value = entity.Value,
            DisplayOrder = entity.DisplayOrder,
            IsActive = entity.IsActive,
            Description = entity.Description,
            CreatedAt = entity.CreatedAtUtc,
            UpdatedAt = entity.UpdatedAtUtc ?? entity.CreatedAtUtc
        };
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _context.LandingPageContents.FindAsync(new object[] { id }, cancellationToken);
        if (entity == null)
        {
            return false;
        }

        _context.LandingPageContents.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
