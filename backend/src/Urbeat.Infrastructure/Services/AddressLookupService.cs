using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Geo;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// CEP and logradouro lookup. The PostgreSQL geographic base is the source of truth; ViaCEP is a
/// display-only fallback and its payload is never persisted. Redis stores a versioned derived cache
/// (<c>urbeat:addr:{datasetVersion}:cep:{cep}</c>) and is discarded on any miss or failure, so a
/// Redis outage only costs latency.
/// </summary>
public sealed class AddressLookupService : IAddressLookupService
{
    public const int MaxMatches = 20;
    public const int MaxStreetResults = 50;
    public static readonly TimeSpan PositiveCacheTtl = TimeSpan.FromDays(30);
    public static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(5);

    private const string CachePrefix = "urbeat:addr";
    private const string NegativeMarker = "__none__";

    private readonly ApplicationDbContext _dbContext;
    private readonly IViaCepService _viaCepService;
    private readonly IGeographicCache _cache;
    private readonly ILogger<AddressLookupService> _logger;

    public AddressLookupService(
        ApplicationDbContext dbContext,
        IViaCepService viaCepService,
        ILogger<AddressLookupService> logger,
        IGeographicCache cache)
    {
        _dbContext = dbContext;
        _viaCepService = viaCepService;
        _logger = logger;
        _cache = cache;
    }

    public async Task<AddressLookupResponseDto?> LookupByCepAsync(string cep, CancellationToken cancellationToken = default)
    {
        var digits = NeighborhoodName.NormalizeZipCode(cep);
        if (digits is null)
        {
            return null;
        }

        var datasetVersion = await GetActiveDatasetVersionAsync(digits, cancellationToken);
        var cached = await ReadCacheAsync(digits, datasetVersion, cancellationToken);
        if (cached is not null)
        {
            return cached == NegativeMarker ? null : JsonSerializer.Deserialize<AddressLookupResponseDto>(cached);
        }

        var local = await LookupLocalAsync(digits, cancellationToken);
        if (local is not null)
        {
            await WriteCacheAsync(digits, local, cancellationToken);
            return local;
        }

        var fallback = await _viaCepService.LookupAsync(digits, cancellationToken);
        if (fallback is null)
        {
            await WriteCacheAsync(digits, null, cancellationToken);
            return null;
        }

        // A ViaCEP miss/hit is never persisted in the authoritative geographic tables.
        return new AddressLookupResponseDto
        {
            Cep = digits,
            Street = fallback.Street,
            Neighborhood = fallback.Neighborhood,
            City = fallback.City,
            State = fallback.State,
            Complement = fallback.Complement,
            Source = "viacep",
            DatasetVersion = datasetVersion,
            MultipleMatches = false
        };
    }

    public async Task<IReadOnlyCollection<StreetSearchResultDto>> SearchStreetsAsync(
        Guid? cityId,
        string? uf,
        string? query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NeighborhoodName.Normalize(query);
        if (normalizedQuery.Length == 0 && cityId is null && string.IsNullOrWhiteSpace(uf))
        {
            // A free-text national scan is not allowed.
            return [];
        }

        var take = Math.Clamp(limit, 1, MaxStreetResults);

        var streets = _dbContext.DeliveryStreets
            .AsNoTracking()
            .Where(x => x.IsActive);

        if (cityId is not null)
        {
            streets = streets.Where(x => x.CityId == cityId);
        }
        else if (!string.IsNullOrWhiteSpace(uf))
        {
            var normalizedUf = uf.Trim().ToUpperInvariant();
            streets = streets.Where(x => x.CityEntity != null && x.CityEntity.Uf == normalizedUf);
        }
        else
        {
            return [];
        }

        if (normalizedQuery.Length > 0)
        {
            streets = streets.Where(x => x.NormalizedName.StartsWith(normalizedQuery));
        }

        return await streets
            .OrderBy(x => x.Name)
            .Take(take)
            .Select(x => new StreetSearchResultDto
            {
                Id = x.Id,
                Name = x.Name,
                Neighborhood = x.NeighborhoodEntity != null ? x.NeighborhoodEntity.Neighborhood : null,
                City = x.CityEntity != null ? x.CityEntity.Name : string.Empty,
                State = x.CityEntity != null ? x.CityEntity.Uf : string.Empty,
                Latitude = x.Latitude,
                Longitude = x.Longitude
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<AddressLookupResponseDto?> LookupLocalAsync(string digits, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.DeliveryStreetZipCodes
            .AsNoTracking()
            .Where(x => x.ZipCode == digits && x.Street != null && x.Street.IsActive)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Street!.Name)
            .Take(MaxMatches)
            .Select(x => new
            {
                Street = x.Street!.Name,
                Neighborhood = x.Street.NeighborhoodEntity != null ? x.Street.NeighborhoodEntity.Neighborhood : string.Empty,
                City = x.Street.CityEntity != null ? x.Street.CityEntity.Name : string.Empty,
                State = x.Street.CityEntity != null ? x.Street.CityEntity.Uf : string.Empty,
                IbgeCode = x.Street.CityEntity != null ? x.Street.CityEntity.IbgeCode : null,
                Latitude = x.Street.Latitude,
                Longitude = x.Street.Longitude
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var matches = rows
            .Select(row => new AddressLookupMatchDto
            {
                Street = row.Street,
                Neighborhood = row.Neighborhood,
                City = row.City,
                State = row.State,
                IbgeCode = row.IbgeCode,
                // Mean coordinate of the logradouro, never an individual household point.
                Latitude = row.Latitude,
                Longitude = row.Longitude
            })
            .ToList();

        var primary = matches[0];
        var version = await GetActiveDatasetVersionForUfAsync(primary.State, cancellationToken);

        return new AddressLookupResponseDto
        {
            Cep = digits,
            Street = primary.Street,
            Neighborhood = primary.Neighborhood,
            City = primary.City,
            State = primary.State,
            IbgeCode = primary.IbgeCode,
            Latitude = primary.Latitude,
            Longitude = primary.Longitude,
            Source = "local",
            DatasetVersion = version,
            MultipleMatches = matches.Count > 1,
            Matches = matches
        };
    }

    private async Task<string?> GetActiveDatasetVersionAsync(string cep, CancellationToken cancellationToken)
    {
        // The neighborhood UF is the natural partition for the dataset pointer; when the CEP is not
        // known locally, the version is resolved after the local query instead.
        if (cep.Length != NeighborhoodName.ZipCodeLength)
        {
            return null;
        }

        return await _dbContext.DeliveryStreetZipCodes
            .AsNoTracking()
            .Where(x => x.ZipCode == cep && x.Street != null && x.Street.CityEntity != null)
            .Select(x => x.Street!.CityEntity!.Uf)
            .FirstOrDefaultAsync(cancellationToken) is { } uf
            ? await GetActiveDatasetVersionForUfAsync(uf, cancellationToken)
            : null;
    }

    private async Task<string?> GetActiveDatasetVersionForUfAsync(string? uf, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uf))
        {
            return null;
        }

        var normalizedUf = uf.Trim().ToUpperInvariant();
        return await _dbContext.ImportDatasets
            .AsNoTracking()
            .Where(x => x.IsActive && x.Uf == normalizedUf)
            .OrderByDescending(x => x.ActivatedAtUtc)
            .Select(x => x.SourceVersion)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> ReadCacheAsync(string cep, string? datasetVersion, CancellationToken cancellationToken)
    {
        if (!_cache.IsAvailable)
        {
            return null;
        }

        try
        {
            return await _cache.GetAsync(datasetVersion, cep, cancellationToken);
        }
        catch (Exception exception)
        {
            // Redis is a derived cache; a failure must never break the lookup.
            _logger.LogWarning(exception, "Geographic address cache read failed for CEP {Cep}.", cep);
            return null;
        }
    }

    private async Task WriteCacheAsync(string cep, AddressLookupResponseDto? value, CancellationToken cancellationToken)
    {
        if (!_cache.IsAvailable)
        {
            return;
        }

        var version = value?.DatasetVersion ?? await GetActiveDatasetVersionAsync(cep, cancellationToken);

        try
        {
            var payload = value is null ? NegativeMarker : JsonSerializer.Serialize(value);
            var ttl = value is null ? NegativeCacheTtl : PositiveCacheTtl;
            await _cache.SetAsync(version, cep, payload, ttl, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Geographic address cache write failed for CEP {Cep}.", cep);
        }
    }

    /// <summary>
    /// Versioned key. The dataset version is part of the namespace, so activating a new import
    /// version invalidates every cached CEP without a scan or a delete storm.
    /// </summary>
    public static string BuildCacheKey(string? datasetVersion, string cep)
    {
        var version = string.IsNullOrWhiteSpace(datasetVersion)
            ? "unversioned"
            : datasetVersion.Trim().ToLowerInvariant();

        return $"{CachePrefix}:{version}:cep:{cep}";
    }
}
