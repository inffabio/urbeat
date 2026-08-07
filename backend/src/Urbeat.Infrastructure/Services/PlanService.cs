using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class PlanService : IPlanService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;

    public PlanService(ApplicationDbContext dbContext, IEfUnitOfWork efUnitOfWork)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
    }

    public async Task<IReadOnlyList<PlanResponseDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Plans
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new PlanResponseDto
            {
                Id = x.Id,
                Name = x.Name,
                Amount = x.Amount,
                Description = x.Description,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlanResponseDto>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Plans
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Amount)
            .ThenBy(x => x.Name)
            .Select(x => new PlanResponseDto
            {
                Id = x.Id,
                Name = x.Name,
                Amount = x.Amount,
                Description = x.Description,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<PlanResponseDto?> CreateAsync(CreatePlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedName = request.Name.Trim();
        var exists = await _dbContext.Plans
            .AsNoTracking()
            .AnyAsync(x => x.Name == normalizedName, cancellationToken);

        if (exists)
        {
            return null;
        }

        var plan = new Plan
        {
            Name = normalizedName,
            Amount = request.Amount,
            Description = request.Description.Trim(),
            IsActive = request.IsActive
        };

        await _dbContext.Plans.AddAsync(plan, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return Map(plan);
    }

    public async Task<PlanResponseDto?> UpdateAsync(Guid planId, UpdatePlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Plans
            .SingleOrDefaultAsync(x => x.Id == planId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var normalizedName = request.Name.Trim();
        var duplicateName = await _dbContext.Plans
            .AsNoTracking()
            .AnyAsync(x => x.Id != planId && x.Name == normalizedName, cancellationToken);

        if (duplicateName)
        {
            return null;
        }

        plan.Name = normalizedName;
        plan.Amount = request.Amount;
        plan.Description = request.Description.Trim();
        plan.MarkAsUpdated();

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return Map(plan);
    }

    public async Task<PlanResponseDto?> UpdateStatusAsync(Guid planId, bool isActive, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Plans
            .SingleOrDefaultAsync(x => x.Id == planId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        plan.IsActive = isActive;
        plan.MarkAsUpdated();
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return Map(plan);
    }

    private static PlanResponseDto Map(Plan plan)
    {
        return new PlanResponseDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Amount = plan.Amount,
            Description = plan.Description,
            IsActive = plan.IsActive
        };
    }
}