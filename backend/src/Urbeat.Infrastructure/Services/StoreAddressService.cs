using System.Globalization;
using System.Net.Http.Json;
using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Services;

public sealed class StoreAddressService : IStoreAddressService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StoreAddressService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public StoreAddressService(
        ApplicationDbContext dbContext,
        IMapper mapper,
        IEfUnitOfWork efUnitOfWork,
        IHttpClientFactory httpClientFactory,
        ILogger<StoreAddressService> logger,
        IServiceScopeFactory scopeFactory,
        IBackgroundJobClient backgroundJobClient)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _efUnitOfWork = efUnitOfWork;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<StoreAddressResponseDto?> GetByStoreAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (store is null)
        {
            return null;
        }

        var address = await _dbContext.StoreAddresses
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);

        return address is null ? null : _mapper.Map<StoreAddressResponseDto>(address);
    }

    public async Task<UpsertStoreAddressResultDto> UpsertAsync(
        Guid ownerUserId,
        Guid storeId,
        UpdateStoreAddressRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            return new UpsertStoreAddressResultDto
            {
                NotFound = true
            };
        }

        if (store.OwnerUserId != ownerUserId)
        {
            await WriteAuditLogAsync(
                ownerUserId,
                "StoreAddressUpsertForbidden",
                nameof(StoreAddress),
                storeId,
                "Store address update denied: user is not the owner.",
                ipAddress,
                cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return new UpsertStoreAddressResultDto
            {
                Forbidden = true
            };
        }

        var address = await _dbContext.StoreAddresses
            .SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);

        if (address is null)
        {
            address = new StoreAddress
            {
                StoreId = storeId
            };

            await _dbContext.StoreAddresses.AddAsync(address, cancellationToken);
        }

        address.Street = request.Street.Trim();
        address.Number = request.Number.Trim();
        address.Neighborhood = request.Neighborhood.Trim();
        address.City = request.City.Trim();
        address.State = request.State.Trim();
        address.ZipCode = request.ZipCode.Trim();
        address.Complement = request.Complement?.Trim();
        address.Reference = request.Reference?.Trim();
        if (request.Latitude.HasValue)
            address.Latitude = request.Latitude;
        if (request.Longitude.HasValue)
            address.Longitude = request.Longitude;
        address.MarkAsUpdated();

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        if (!address.Latitude.HasValue || !address.Longitude.HasValue)
        {
            var addressId = address.Id;
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<StoreAddressService>>();
                await GeocodeInScopeAsync(dbContext, httpClientFactory, logger, addressId);
            });
        }

        if (!string.IsNullOrWhiteSpace(address.City) && !string.IsNullOrWhiteSpace(address.State))
        {
            _backgroundJobClient.Enqueue<ImportStoreNeighborhoodsJob>(
                job => job.ExecuteAsync(address.City, address.State, storeId, 0));
        }

        await WriteAuditLogAsync(
            ownerUserId,
            "StoreAddressUpserted",
            nameof(StoreAddress),
            address.Id,
            "Store address saved successfully.",
            ipAddress,
            cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpsertStoreAddressResultDto
        {
            Address = _mapper.Map<StoreAddressResponseDto>(address)
        };
    }

    private static async Task GeocodeInScopeAsync(ApplicationDbContext dbContext, IHttpClientFactory httpClientFactory, ILogger<StoreAddressService> logger, Guid addressId)
    {
        try
        {
            var address = await dbContext.StoreAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == addressId);
            if (address is null) return;

            var coords = await TryGeocodeAddressAsync(httpClientFactory, address);
            if (coords is null) return;

            var (lat, lon) = coords.Value;

            var entity = await dbContext.StoreAddresses.SingleOrDefaultAsync(x => x.Id == addressId);
            if (entity is not null && !entity.Latitude.HasValue)
            {
                entity.Latitude = lat;
                entity.Longitude = lon;
                entity.MarkAsUpdated();
                await dbContext.SaveChangesAsync();
                logger.LogInformation("Geocoded store address {Id}: ({Lat}, {Lon})", addressId, lat, lon);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to geocode store address {Id}", addressId);
        }
    }

    private static async Task<(double lat, double lon)?> TryGeocodeAddressAsync(IHttpClientFactory httpClientFactory, StoreAddress address)
    {
        var client = httpClientFactory.CreateClient("Nominatim");

        var queries = new[]
        {
            $"{address.Street}, {address.Neighborhood}, {address.City}, {address.State}, Brasil",
            $"{address.Street}, {address.City}, {address.State}, Brasil",
            $"{address.Neighborhood}, {address.City}, {address.State}, Brasil",
            $"{address.City}, {address.State}, Brasil"
        };

        foreach (var query in queries)
        {
            if (string.IsNullOrWhiteSpace(query.Replace(",", "").Trim()))
                continue;

            var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=jsonv2&limit=1";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await client.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode) continue;

            var results = await response.Content.ReadFromJsonAsync<List<NominatimGeoResult>>(cancellationToken: cts.Token);
            if (results is null || results.Count == 0) continue;

            var first = results[0];
            if (double.TryParse(first.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(first.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                return (lat, lon);
            }
        }

        return null;
    }

    private sealed class NominatimGeoResult
    {
        public string Lat { get; init; } = string.Empty;
        public string Lon { get; init; } = string.Empty;
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