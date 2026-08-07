using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class CustomerAddressService : ICustomerAddressService
{
    private const int MaxAddressesPerCustomer = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IViaCepService _viaCepService;

    public CustomerAddressService(
        ApplicationDbContext dbContext,
        IMapper mapper,
        IEfUnitOfWork efUnitOfWork,
        IViaCepService viaCepService)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _efUnitOfWork = efUnitOfWork;
        _viaCepService = viaCepService;
    }

    public async Task<IReadOnlyCollection<CustomerAddressResponseDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var addresses = await _dbContext.CustomerAddresses
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyCollection<CustomerAddressResponseDto>>(addresses);
    }

    public async Task<UpsertCustomerAddressResultDto> CreateAsync(
        Guid userId,
        UpsertCustomerAddressRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var count = await _dbContext.CustomerAddresses.CountAsync(x => x.UserId == userId, cancellationToken);
        if (count >= MaxAddressesPerCustomer)
        {
            return new UpsertCustomerAddressResultDto
            {
                LimitReached = true
            };
        }

        var normalized = await NormalizeAddressAsync(request, cancellationToken);

        if (normalized.IsPrimary)
        {
            await UnsetCurrentPrimaryAsync(userId, cancellationToken);
        }
        else if (count == 0)
        {
            normalized = normalized with { IsPrimary = true };
        }

        var entity = new CustomerAddress
        {
            UserId = userId,
            Cep = normalized.Cep,
            Street = normalized.Street,
            Number = normalized.Number,
            Neighborhood = normalized.Neighborhood,
            City = normalized.City,
            State = normalized.State,
            Complement = normalized.Complement,
            Reference = normalized.Reference,
            IsPrimary = normalized.IsPrimary
        };

        await _dbContext.CustomerAddresses.AddAsync(entity, cancellationToken);
        await WriteAuditLogAsync(userId, "CustomerAddressCreated", nameof(CustomerAddress), entity.Id, "Customer address created.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpsertCustomerAddressResultDto
        {
            Address = _mapper.Map<CustomerAddressResponseDto>(entity)
        };
    }

    public async Task<UpsertCustomerAddressResultDto> UpdateAsync(
        Guid userId,
        Guid addressId,
        UpsertCustomerAddressRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CustomerAddresses.SingleOrDefaultAsync(x => x.Id == addressId && x.UserId == userId, cancellationToken);
        if (entity is null)
        {
            return new UpsertCustomerAddressResultDto
            {
                NotFound = true
            };
        }

        var normalized = await NormalizeAddressAsync(request, cancellationToken);

        if (normalized.IsPrimary)
        {
            await UnsetCurrentPrimaryAsync(userId, cancellationToken);
        }

        entity.Cep = normalized.Cep;
        entity.Street = normalized.Street;
        entity.Number = normalized.Number;
        entity.Neighborhood = normalized.Neighborhood;
        entity.City = normalized.City;
        entity.State = normalized.State;
        entity.Complement = normalized.Complement;
        entity.Reference = normalized.Reference;
        entity.IsPrimary = normalized.IsPrimary;
        entity.MarkAsUpdated();

        await WriteAuditLogAsync(userId, "CustomerAddressUpdated", nameof(CustomerAddress), entity.Id, "Customer address updated.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpsertCustomerAddressResultDto
        {
            Address = _mapper.Map<CustomerAddressResponseDto>(entity)
        };
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid addressId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CustomerAddresses.SingleOrDefaultAsync(x => x.Id == addressId && x.UserId == userId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        var wasPrimary = entity.IsPrimary;

        _dbContext.CustomerAddresses.Remove(entity);

        if (wasPrimary)
        {
            var next = await _dbContext.CustomerAddresses
                .Where(x => x.UserId == userId && x.Id != addressId)
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.IsPrimary = true;
                next.MarkAsUpdated();
            }
        }

        await WriteAuditLogAsync(userId, "CustomerAddressDeleted", nameof(CustomerAddress), addressId, "Customer address deleted.", ipAddress, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<NormalizedAddress> NormalizeAddressAsync(UpsertCustomerAddressRequestDto request, CancellationToken cancellationToken)
    {
        var cepDigits = new string(request.Cep.Where(char.IsDigit).ToArray());
        var street = request.Street?.Trim() ?? string.Empty;
        var neighborhood = request.Neighborhood?.Trim() ?? string.Empty;
        var city = request.City?.Trim() ?? string.Empty;
        var state = request.State?.Trim().ToUpperInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(neighborhood) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state))
        {
            var viaCep = await _viaCepService.LookupAsync(cepDigits, cancellationToken);
            if (viaCep is not null)
            {
                street = string.IsNullOrWhiteSpace(street) ? viaCep.Street : street;
                neighborhood = string.IsNullOrWhiteSpace(neighborhood) ? viaCep.Neighborhood : neighborhood;
                city = string.IsNullOrWhiteSpace(city) ? viaCep.City : city;
                state = string.IsNullOrWhiteSpace(state) ? viaCep.State : state;
            }
        }

        return new NormalizedAddress(
            Cep: cepDigits,
            Number: request.Number.Trim(),
            Street: street,
            Neighborhood: neighborhood,
            City: city,
            State: state,
            Complement: request.Complement?.Trim(),
            Reference: request.Reference?.Trim(),
            IsPrimary: request.IsPrimary);
    }

    private async Task UnsetCurrentPrimaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var currentPrimary = await _dbContext.CustomerAddresses
            .Where(x => x.UserId == userId && x.IsPrimary)
            .ToListAsync(cancellationToken);

        foreach (var address in currentPrimary)
        {
            address.IsPrimary = false;
            address.MarkAsUpdated();
        }
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

    private sealed record NormalizedAddress(
        string Cep,
        string Number,
        string Street,
        string Neighborhood,
        string City,
        string State,
        string? Complement,
        string? Reference,
        bool IsPrimary);
}
