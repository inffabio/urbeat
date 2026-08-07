using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class StoreBusinessHoursService : IStoreBusinessHoursService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IEfUnitOfWork _efUnitOfWork;

    public StoreBusinessHoursService(ApplicationDbContext dbContext, IMapper mapper, IEfUnitOfWork efUnitOfWork)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _efUnitOfWork = efUnitOfWork;
    }

    public async Task<StoreBusinessHoursResponseDto?> GetAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (store is null)
        {
            return null;
        }

        var entities = await _dbContext.StoreBusinessHours
            .AsNoTracking()
            .Include(x => x.Shifts)
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.DayOfWeek)
            .ToListAsync(cancellationToken);

        var items = _mapper.Map<IReadOnlyCollection<StoreBusinessHourItemDto>>(entities);

        return new StoreBusinessHoursResponseDto
        {
            StoreId = storeId,
            Items = items
        };
    }

    public async Task<(bool NotFound, bool Forbidden, StoreBusinessHoursResponseDto? Hours)> UpsertAsync(
        Guid ownerUserId,
        Guid storeId,
        UpsertStoreBusinessHoursRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            return (true, false, null);
        }

        if (store.OwnerUserId != ownerUserId)
        {
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreBusinessHoursUpsertForbidden",
                nameof(StoreBusinessHour),
                storeId,
                "Store business hours update denied: user is not the owner.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return (false, true, null);
        }

        var existing = await _dbContext.StoreBusinessHours
            .Include(x => x.Shifts)
            .Where(x => x.StoreId == storeId)
            .ToListAsync(cancellationToken);

        _dbContext.StoreBusinessHours.RemoveRange(existing);

        var newHours = request.Items.Select(item =>
        {
            var hour = new StoreBusinessHour
            {
                StoreId = storeId,
                DayOfWeek = item.DayOfWeek,
                IsOpen = item.IsOpen
            };

            if (item.IsOpen && item.Shifts.Count > 0)
            {
                foreach (var shiftDto in item.Shifts)
                {
                    hour.Shifts.Add(new StoreBusinessHourShift
                    {
                        StartTime = shiftDto.StartTime,
                        EndTime = shiftDto.EndTime
                    });
                }
            }

            return hour;
        }).ToList();

        await _dbContext.StoreBusinessHours.AddRangeAsync(newHours, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(
            ownerUserId,
            "StoreBusinessHoursUpserted",
            nameof(StoreBusinessHour),
            storeId,
            "Store business hours saved successfully.",
            ipAddress,
            cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return (false, false, new StoreBusinessHoursResponseDto
        {
            StoreId = storeId,
            Items = _mapper.Map<IReadOnlyCollection<StoreBusinessHourItemDto>>(newHours.OrderBy(x => x.DayOfWeek).ToList())
        });
    }

    private async Task WriteAuditLogAsync(
        Guid userId,
        string auditEvent,
        string entity,
        Guid entityId,
        string description,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = userId,
            Event = auditEvent,
            Entity = entity,
            EntityId = entityId,
            Description = description,
            IpAddress = ipAddress
        }, cancellationToken);
    }
}
