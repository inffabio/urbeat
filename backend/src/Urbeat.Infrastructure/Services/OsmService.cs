using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Urbeat.Infrastructure.Services;

public sealed class OsmService : IOsmService
{
    private static readonly AsyncRetryPolicy _retryPolicy = Policy
        .Handle<HttpRequestException>()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (ex, ts, attempt, _) =>
            {
                Console.WriteLine($"[OsmService] Retry {attempt}/3 after {ts.TotalSeconds:F0}s — {ex.Message}");
            });
    private static readonly Dictionary<string, string> UfToState = new()
    {
        ["AC"] = "Acre",
        ["AL"] = "Alagoas",
        ["AP"] = "Amapá",
        ["AM"] = "Amazonas",
        ["BA"] = "Bahia",
        ["CE"] = "Ceará",
        ["DF"] = "Distrito Federal",
        ["ES"] = "Espírito Santo",
        ["GO"] = "Goiás",
        ["MA"] = "Maranhão",
        ["MT"] = "Mato Grosso",
        ["MS"] = "Mato Grosso do Sul",
        ["MG"] = "Minas Gerais",
        ["PA"] = "Pará",
        ["PB"] = "Paraíba",
        ["PR"] = "Paraná",
        ["PE"] = "Pernambuco",
        ["PI"] = "Piauí",
        ["RJ"] = "Rio de Janeiro",
        ["RN"] = "Rio Grande do Norte",
        ["RS"] = "Rio Grande do Sul",
        ["RO"] = "Rondônia",
        ["RR"] = "Roraima",
        ["SC"] = "Santa Catarina",
        ["SP"] = "São Paulo",
        ["SE"] = "Sergipe",
        ["TO"] = "Tocantins",
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OsmService> _logger;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _cityLocks = new();

    public OsmService(ApplicationDbContext dbContext, HttpClient httpClient, IHttpClientFactory httpClientFactory, ILogger<OsmService> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ImportNeighborhoodsResultDto> ImportNeighborhoodsByCepAsync(
        string cep, CancellationToken cancellationToken = default)
    {
        var cepDigits = new string(cep.Where(char.IsDigit).ToArray());
        if (cepDigits.Length != 8)
            throw new InvalidOperationException("CEP deve conter 8 dígitos.");

        var viaCep = await LookupViaCepAsync(cepDigits, cancellationToken);
        if (viaCep is null || viaCep.Erro == "true")
            throw new InvalidOperationException("CEP não encontrado.");

        var cityName = viaCep.Localidade ?? string.Empty;
        var uf = (viaCep.Uf ?? string.Empty).ToUpperInvariant();
        var ibgeCode = viaCep.Ibge;

        if (string.IsNullOrWhiteSpace(cityName) || string.IsNullOrWhiteSpace(uf))
            throw new InvalidOperationException("CEP retornou dados incompletos.");

        var city = await UpsertCityAsync(cityName, uf, ibgeCode, cancellationToken);

        if (string.IsNullOrWhiteSpace(city.OsmAreaId))
        {
            var stateName = UfToState.GetValueOrDefault(uf, uf);
            var municipioOsm = await LookupCityInNominatimAsync(cityName, stateName, cancellationToken);
            if (municipioOsm is null)
                throw new InvalidOperationException($"Município não encontrado no OSM: {cityName}/{uf}");

            var areaId = 3600000000L + municipioOsm.OsmId;
            city.OsmId = municipioOsm.OsmId.ToString(CultureInfo.InvariantCulture);
            city.OsmAreaId = areaId.ToString(CultureInfo.InvariantCulture);
            city.MarkAsUpdated();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await _retryPolicy.ExecuteAsync(
            async ct => await ImportNeighborhoodsFromOverpassAsync(city, null, ct),
            cancellationToken);
    }

    public async Task<ImportNeighborhoodsResultDto> ImportNeighborhoodsByCityNameAsync(
        string cityName, string uf, Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        var normalizedUf = uf.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cityName) || string.IsNullOrWhiteSpace(normalizedUf))
            throw new InvalidOperationException("Cidade e UF são obrigatórios.");

        var city = await UpsertCityAsync(cityName.Trim(), normalizedUf, null, cancellationToken);

        var existingCount = await _dbContext.Set<DeliveryNeighborhood>()
            .CountAsync(x => x.CityId == city.Id && x.IsActive, cancellationToken);

        if (existingCount > 0)
        {
            _logger.LogInformation("City {CityName} already has {Count} cached neighborhoods, skipping OSM import", cityName, existingCount);
            return new ImportNeighborhoodsResultDto
            {
                City = cityName,
                Uf = normalizedUf,
                Found = existingCount,
                Created = 0,
                Updated = 0,
                Ignored = 0
            };
        }

        var cityLock = _cityLocks.GetOrAdd(city.Id, _ => new SemaphoreSlim(1, 1));
        await cityLock.WaitAsync(cancellationToken);
        try
        {
            existingCount = await _dbContext.Set<DeliveryNeighborhood>()
                .CountAsync(x => x.CityId == city.Id && x.IsActive, cancellationToken);

            if (existingCount > 0)
            {
                _logger.LogInformation("City {CityName} imported by another process while waiting, skipping", cityName);
                return new ImportNeighborhoodsResultDto
                {
                    City = cityName,
                    Uf = normalizedUf,
                    Found = existingCount,
                    Created = 0,
                    Updated = 0,
                    Ignored = 0
                };
            }

            if (string.IsNullOrWhiteSpace(city.OsmAreaId))
            {
                var stateName = UfToState.GetValueOrDefault(normalizedUf, normalizedUf);
                var municipioOsm = await LookupCityInNominatimAsync(city.Name, stateName, cancellationToken);
                if (municipioOsm is null)
                    throw new InvalidOperationException($"Município não encontrado no OSM: {cityName}/{normalizedUf}");

                var areaId = 3600000000L + municipioOsm.OsmId;
                city.OsmId = municipioOsm.OsmId.ToString(CultureInfo.InvariantCulture);
                city.OsmAreaId = areaId.ToString(CultureInfo.InvariantCulture);
                city.MarkAsUpdated();
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return await _retryPolicy.ExecuteAsync(
                async ct => await ImportNeighborhoodsFromOverpassAsync(city, storeId, ct),
                cancellationToken);
        }
        finally
        {
            cityLock.Release();
        }
    }

    public async Task<ImportNeighborhoodsResultDto> ImportNeighborhoodsByCityIdAsync(
        Guid cityId, CancellationToken cancellationToken = default)
    {
        var city = await _dbContext.Set<City>().SingleOrDefaultAsync(x => x.Id == cityId, cancellationToken);
        if (city is null)
            throw new InvalidOperationException("Cidade não encontrada.");

        if (string.IsNullOrWhiteSpace(city.OsmAreaId))
        {
            var stateName = UfToState.GetValueOrDefault(city.Uf, city.Uf);
            var municipioOsm = await LookupCityInNominatimAsync(city.Name, stateName, cancellationToken);
            if (municipioOsm is null)
                throw new InvalidOperationException($"Município não encontrado no OSM: {city.Name}/{city.Uf}");

            var areaId = 3600000000L + municipioOsm.OsmId;
            city.OsmId = municipioOsm.OsmId.ToString(CultureInfo.InvariantCulture);
            city.OsmAreaId = areaId.ToString(CultureInfo.InvariantCulture);
            city.MarkAsUpdated();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await _retryPolicy.ExecuteAsync(
            async ct => await ImportNeighborhoodsFromOverpassAsync(city, null, ct),
            cancellationToken);
    }

    public async Task<NeighborhoodMapResponseDto> GetNeighborhoodsMapAsync(
        Guid cityId, Guid? storeId, CancellationToken cancellationToken = default)
    {
        var city = await _dbContext.Set<City>().SingleOrDefaultAsync(x => x.Id == cityId, cancellationToken);
        if (city is null)
            throw new InvalidOperationException("Cidade não encontrada.");

        var neighborhoods = await _dbContext.Set<DeliveryNeighborhood>()
            .AsNoTracking()
            .Where(x => x.CityId == cityId)
            .ToListAsync(cancellationToken);

        var storeAreas = new Dictionary<string, StoreDeliveryArea>();
        if (storeId.HasValue)
        {
            var areas = await _dbContext.Set<StoreDeliveryArea>()
                .AsNoTracking()
                .Where(x => x.StoreId == storeId.Value)
                .ToListAsync(cancellationToken);

            foreach (var a in areas)
                storeAreas[NormalizeText(a.Neighborhood)] = a;
        }

        var items = new List<NeighborhoodMapItemDto>();
        var withoutCoords = new List<NeighborhoodWithoutCoordinatesDto>();

        foreach (var n in neighborhoods)
        {
            var areaKey = NormalizeText(n.Neighborhood);
            var storeArea = storeAreas.GetValueOrDefault(areaKey);

            if (storeArea is null)
                continue;

            if (n.Latitude.HasValue && n.Longitude.HasValue)
            {
                items.Add(new NeighborhoodMapItemDto
                {
                    NeighborhoodId = n.Id,
                    Name = n.Neighborhood,
                    Latitude = n.Latitude,
                    Longitude = n.Longitude,
                    Rate = storeArea?.DeliveryFee ?? 0,
                    Active = n.IsActive
                });
            }
            else
            {
                withoutCoords.Add(new NeighborhoodWithoutCoordinatesDto
                {
                    NeighborhoodId = n.Id,
                    Name = n.Neighborhood
                });
            }
        }

        return new NeighborhoodMapResponseDto
        {
            City = new CityMapInfoDto
            {
                Id = city.Id,
                Name = city.Name,
                Uf = city.Uf
            },
            Items = items,
            WithoutCoordinates = withoutCoords
        };
    }

    public async Task<IReadOnlyCollection<NeighborhoodSearchResultDto>> SearchNeighborhoodsAsync(
        Guid cityId, Guid? storeId, string? search, bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<DeliveryNeighborhood>()
            .AsNoTracking()
            .Where(x => x.CityId == cityId);

        if (activeOnly)
            query = query.Where(x => x.IsActive);

        var neighborhoods = await query.ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = NormalizeText(search);
            neighborhoods = neighborhoods
                .Where(x => x.NormalizedName.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var storeAreas = new Dictionary<string, StoreDeliveryArea>();
        if (storeId.HasValue)
        {
            var areas = await _dbContext.Set<StoreDeliveryArea>()
                .AsNoTracking()
                .Where(x => x.StoreId == storeId.Value)
                .ToListAsync(cancellationToken);

            foreach (var a in areas)
                storeAreas[NormalizeText(a.Neighborhood)] = a;
        }

        return neighborhoods
            .OrderBy(x => x.Neighborhood, StringComparer.Create(new CultureInfo("pt-BR"), false))
            .Select(n =>
            {
                var areaKey = NormalizeText(n.Neighborhood);
                var storeArea = storeAreas.GetValueOrDefault(areaKey);

                return new NeighborhoodSearchResultDto
                {
                    Id = n.Id,
                    Name = n.Neighborhood,
                    Latitude = n.Latitude,
                    Longitude = n.Longitude,
                    IsActive = n.IsActive,
                    FreightRate = storeArea is null ? null : new NeighborhoodFreightInfoDto
                    {
                        Id = storeArea.Id,
                        Rate = storeArea.DeliveryFee,
                    }
                };
            })
            .ToList();
    }

    public async Task<IReadOnlyCollection<CityResponseDto>> GetCitiesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<City>()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new CityResponseDto
            {
                Id = x.Id,
                Name = x.Name,
                Uf = x.Uf
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasNeighborhoodsForCityAsync(Guid cityId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<DeliveryNeighborhood>()
            .AnyAsync(x => x.CityId == cityId && x.IsActive, cancellationToken);
    }

    private async Task<ImportNeighborhoodsResultDto> ImportNeighborhoodsFromOverpassAsync(
        City city, Guid? storeId, CancellationToken cancellationToken)
    {
        var elements = await QueryOverpassApiAsync(city.OsmAreaId!, cancellationToken);
        var bairrosRaw = NormalizeOsmElements(elements);
        var bairros = RemoveDuplicatesByName(bairrosRaw)
            .OrderBy(x => x.Name, StringComparer.Create(new CultureInfo("pt-BR"), false))
            .ToList();

        double? storeLat = null, storeLon = null;
        double? maxRadius = null;

        if (storeId.HasValue)
        {
            var store = await _dbContext.Stores.SingleOrDefaultAsync(x => x.Id == storeId.Value, cancellationToken);
            if (store?.MaxDeliveryRadiusKm > 0)
            {
                maxRadius = store.MaxDeliveryRadiusKm.Value;
                var addr = await _dbContext.StoreAddresses
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.StoreId == storeId.Value, cancellationToken);
                if (addr?.Latitude != null && addr.Longitude != null)
                {
                    storeLat = addr.Latitude;
                    storeLon = addr.Longitude;
                }
            }
        }

        if (storeLat.HasValue && storeLon.HasValue && maxRadius.HasValue)
        {
            bairros = bairros
                .Where(b => b.Latitude.HasValue && b.Longitude.HasValue &&
                    HaversineKm(storeLat.Value, storeLon.Value, b.Latitude.Value, b.Longitude.Value) <= maxRadius.Value)
                .ToList();

            var radiusElements = await QueryOverpassRadiusAsync(storeLat.Value, storeLon.Value, maxRadius.Value, cancellationToken);
            var radiusBairros = NormalizeOsmElements(radiusElements);
            radiusBairros = RemoveDuplicatesByName(radiusBairros)
                .Where(b => b.Latitude.HasValue && b.Longitude.HasValue &&
                    HaversineKm(storeLat.Value, storeLon.Value, b.Latitude.Value, b.Longitude.Value) <= maxRadius.Value)
                .ToList();

            var existingRadialNames = bairros.ToDictionary(b => b.NormalizedName, b => b);
            foreach (var rb in radiusBairros)
            {
                if (!existingRadialNames.ContainsKey(rb.NormalizedName))
                {
                    bairros.Add(rb);
                    existingRadialNames[rb.NormalizedName] = rb;
                }
            }
        }

        var existingNeighborhoods = await _dbContext.Set<DeliveryNeighborhood>()
            .Where(x => x.CityId == city.Id)
            .ToListAsync(cancellationToken);

        var existingByName = new Dictionary<string, DeliveryNeighborhood>();
        foreach (var existing in existingNeighborhoods)
            existingByName[existing.NormalizedName] = existing;

        int created = 0, updated = 0, ignored = 0;

        foreach (var bairro in bairros)
        {
            City? bairroCity = city;
            var bairroCityName = bairro.OsmCityName;
            var osmCityDiffers = !string.IsNullOrWhiteSpace(bairroCityName)
                && !string.Equals(bairroCityName, city.Name, StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(bairroCityName) || !IsValidCityName(bairroCityName) || osmCityDiffers)
            {
                if (bairro.Latitude.HasValue && bairro.Longitude.HasValue)
                {
                    var resolvedCity = await ReverseGeocodeCityAsync(bairro.Latitude.Value, bairro.Longitude.Value, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(resolvedCity) && IsValidCityName(resolvedCity))
                        bairroCityName = resolvedCity;
                }

                if (!IsValidCityName(bairroCityName))
                    bairroCityName = null;
            }

            if (!string.IsNullOrWhiteSpace(bairroCityName) &&
                !string.Equals(bairroCityName, city.Name, StringComparison.OrdinalIgnoreCase))
            {
                bairroCity = await UpsertCityAsync(bairroCityName, city.Uf, null, cancellationToken);
            }

            var existingNeighborhoodsForCity = bairroCity.Id == city.Id
                ? existingNeighborhoods
                : await _dbContext.Set<DeliveryNeighborhood>()
                    .Where(x => x.CityId == bairroCity.Id)
                    .ToListAsync(cancellationToken);

            var existing = existingNeighborhoodsForCity
                .FirstOrDefault(x => x.NormalizedName == bairro.NormalizedName);

            if (existing is not null)
            {
                if (!existing.Latitude.HasValue && bairro.Latitude.HasValue)
                {
                    existing.Latitude = bairro.Latitude;
                    existing.Longitude = bairro.Longitude;
                    existing.OsmId = bairro.OsmId;
                    existing.OsmType = bairro.OsmType;
                    existing.PlaceType = bairro.PlaceType;
                    existing.MarkAsUpdated();
                    updated++;
                }
                else
                {
                    ignored++;
                }
            }
            else
            {
                var entity = new DeliveryNeighborhood
                {
                    CityId = bairroCity.Id,
                    City = bairroCity.Name,
                    Neighborhood = bairro.Name,
                    NormalizedName = bairro.NormalizedName,
                    OsmId = bairro.OsmId,
                    OsmType = bairro.OsmType,
                    PlaceType = bairro.PlaceType,
                    Latitude = bairro.Latitude,
                    Longitude = bairro.Longitude,
                    Source = "openstreetmap",
                    IsActive = true
                };
                var inserted = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "DeliveryNeighborhoods"
                        ("Id", "CityId", "City", "Neighborhood", "NormalizedName", "OsmId", "OsmType", "PlaceType", "Boundary", "AdminLevel", "Latitude", "Longitude", "Source", "IsActive", "CreatedAtUtc")
                    VALUES
                        ({entity.Id}, {entity.CityId}, {entity.City}, {entity.Neighborhood}, {entity.NormalizedName}, {entity.OsmId}, {entity.OsmType}, {entity.PlaceType}, {entity.Boundary}, {entity.AdminLevel}, {entity.Latitude}, {entity.Longitude}, {entity.Source}, {entity.IsActive}, {entity.CreatedAtUtc})
                    ON CONFLICT ("Neighborhood", "City") DO NOTHING
                    """, cancellationToken);

                if (inserted > 0)
                {
                    existingNeighborhoodsForCity.Add(entity);
                    created++;
                }
                else
                {
                    ignored++;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (storeId.HasValue && maxRadius.HasValue)
        {
            var store = await _dbContext.Stores.SingleOrDefaultAsync(x => x.Id == storeId.Value, cancellationToken);
            if (store is not null)
            {
                store.LastImportedRadiusKm = maxRadius;
                store.MarkAsUpdated();
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return new ImportNeighborhoodsResultDto
        {
            City = city.Name,
            Uf = city.Uf,
            Found = bairros.Count,
            Created = created,
            Updated = updated,
            Ignored = ignored
        };
    }

    private async Task<City> UpsertCityAsync(string name, string uf, string? ibgeCode,
        CancellationToken cancellationToken)
    {
        var city = await _dbContext.Set<City>()
            .SingleOrDefaultAsync(x => x.Name == name && x.Uf == uf, cancellationToken);

        if (city is null)
        {
            city = new City
            {
                Name = name,
                Uf = uf,
                IbgeCode = ibgeCode
            };
            await _dbContext.Set<City>().AddAsync(city, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(ibgeCode) && string.IsNullOrWhiteSpace(city.IbgeCode))
        {
            city.IbgeCode = ibgeCode;
            city.MarkAsUpdated();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return city;
    }

    private async Task<ViaCepPayload?> LookupViaCepAsync(string cep, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/ws/{cep}/json/", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<ViaCepPayload>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ViaCEP lookup failed for {Cep}", cep);
            return null;
        }
    }

    private async Task<NominatimResult?> LookupCityInNominatimAsync(string city, string state,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://nominatim.openstreetmap.org/search" +
                $"?city={Uri.EscapeDataString(city)}" +
                $"&state={Uri.EscapeDataString(state)}" +
                $"&country=Brasil" +
                $"&countrycodes=br" +
                $"&format=jsonv2" +
                $"&addressdetails=1" +
                $"&limit=10";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            _logger.LogInformation("Nominatim HTTP {Status} for {City}/{State}", response.StatusCode, city, state);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nominatim returned non-success status {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Nominatim response length: {Length}", json.Length);

            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var results = JsonSerializer.Deserialize<List<NominatimResult>>(json, options);
            if (results is null)
            {
                _logger.LogWarning("Nominatim deserialization returned null");
                return null;
            }

            _logger.LogInformation("Nominatim returned {Count} results", results.Count);

            foreach (var r in results)
                _logger.LogInformation("Nominatim result: osm_type={OsmType}, category={Category}, type={Type}, osm_id={OsmId}",
                    r.OsmType, r.Category, r.Type, r.OsmId);

            var match = results.FirstOrDefault(item =>
                item.OsmType == "relation" &&
                item.Category == "boundary" &&
                item.Type == "administrative");

            if (match is null)
                _logger.LogWarning("No administrative boundary match in {Count} results", results.Count);

            return match;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nominatim lookup failed for {City}/{State}", city, state);
            return null;
        }
    }

    private async Task<List<OsmElement>> QueryOverpassApiAsync(string areaId,
        CancellationToken cancellationToken)
    {
        var query = $"""
            [out:json][timeout:30];

            area({areaId})->.municipio;

            (
              nwr(area.municipio)["place"~"^(neighbourhood|suburb|quarter|city_district|locality|hamlet|village)$"];
            );

            out center tags;
            """;

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["data"] = query
            });
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded")
            {
                CharSet = "UTF-8"
            };

            var response = await _httpClient.PostAsync("https://overpass-api.de/api/interpreter", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Overpass API returned {StatusCode} for area {AreaId}: {Body}", (int)response.StatusCode, areaId, body);
                throw new InvalidOperationException($"OpenStreetMap indisponivel (HTTP {(int)response.StatusCode}). Tente novamente em alguns segundos.");
            }

            var data = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken: cancellationToken);
            return data?.Elements ?? new List<OsmElement>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Overpass API query failed for area {AreaId}", areaId);
            throw new InvalidOperationException("Falha ao consultar OpenStreetMap. Verifique sua conexao e tente novamente.");
        }
    }

    private async Task<List<OsmElement>> QueryOverpassRadiusAsync(double lat, double lon, double radiusKm,
        CancellationToken cancellationToken)
    {
        var radiusMeters = (int)(radiusKm * 1000);
        var query = $"""
            [out:json][timeout:30];

            (
              nwr(around:{radiusMeters},{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)})["place"~"^(neighbourhood|suburb|quarter|city_district|locality|hamlet|village)$"];
            );

            out center tags;
            """;

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["data"] = query
            });
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded")
            {
                CharSet = "UTF-8"
            };

            var response = await _httpClient.PostAsync("https://overpass-api.de/api/interpreter", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Overpass radius query returned {StatusCode} for ({Lat},{Lon}) r={Radius}km: {Body}", (int)response.StatusCode, lat, lon, radiusKm, body);
                throw new InvalidOperationException($"OpenStreetMap indisponivel (HTTP {(int)response.StatusCode}). Tente novamente em alguns segundos.");
            }

            var data = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken: cancellationToken);
            return data?.Elements ?? new List<OsmElement>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Overpass radius query failed for ({Lat},{Lon}) r={Radius}km", lat, lon, radiusKm);
            throw new InvalidOperationException("Falha ao consultar OpenStreetMap. Verifique sua conexao e tente novamente.");
        }
    }

    private static List<OsmBairro> NormalizeOsmElements(List<OsmElement> elements)
    {
        return elements
            .Where(item => item.Tags != null && item.Tags.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n))
            .Select(item =>
            {
                var neighborhoodName = item.Tags!["name"].Trim();

                var osmCity = item.Tags.TryGetValue("addr:city", out var ac) ? ac
                    : item.Tags.TryGetValue("is_in:city", out var ic) ? ic
                    : item.Tags.TryGetValue("is_in", out var ii) ? ExtractCityFromIsIn(ii)
                    : item.Tags.TryGetValue("wikipedia", out var wp) ? ExtractCityFromWikipedia(wp)
                    : null;

                return new OsmBairro(
                    OsmId: item.Id.ToString(CultureInfo.InvariantCulture),
                    OsmType: item.Type ?? string.Empty,
                    Name: neighborhoodName,
                    NormalizedName: NormalizeText(neighborhoodName),
                    PlaceType: item.Tags.TryGetValue("place", out var p) ? p : null,
                    Boundary: item.Tags.TryGetValue("boundary", out var b) ? b : null,
                    AdminLevel: item.Tags.TryGetValue("admin_level", out var a) ? a : null,
                    Latitude: item.Lat ?? item.Center?.Lat,
                    Longitude: item.Lon ?? item.Center?.Lon,
                    OsmCityName: osmCity
                );
            })
            .ToList();
    }

    private static List<OsmBairro> RemoveDuplicatesByName(List<OsmBairro> list)
    {
        var map = new Dictionary<string, OsmBairro>();

        foreach (var item in list)
        {
            if (!map.ContainsKey(item.NormalizedName))
            {
                map[item.NormalizedName] = item;
            }
            else
            {
                var existing = map[item.NormalizedName];
                if (existing.Latitude is null && item.Latitude is not null)
                    map[item.NormalizedName] = item;
            }
        }

        return map.Values.ToList();
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static bool IsValidCityName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var lower = name.ToLowerInvariant();
        var invalidWords = new[]
        {
            "bairro", "distrito de", "subúrbio", "município de",
            "região", "vila", "aldeia", "povoado", "localidade",
            "suburb", "district", "quarter", "neighbourhood", "village"
        };

        foreach (var word in invalidWords)
        {
            if (lower.Contains(word))
                return false;
        }

        return true;
    }

    private static long _lastReverseGeocodeTick;

    private async Task<string?> ReverseGeocodeCityAsync(double lat, double lon, CancellationToken cancellationToken)
    {
        var maxRetries = 3;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var elapsed = Stopwatch.GetElapsedTime(_lastReverseGeocodeTick);
                var minInterval = TimeSpan.FromSeconds(2);
                if (elapsed < minInterval)
                    await Task.Delay(minInterval - elapsed, CancellationToken.None);

                var url = $"https://nominatim.openstreetmap.org/reverse?lat={lat.ToString(CultureInfo.InvariantCulture)}&lon={lon.ToString(CultureInfo.InvariantCulture)}&format=jsonv2&addressdetails=1&zoom=18";

                var client = _httpClientFactory.CreateClient("Nominatim");
                var response = await client.GetAsync(url, CancellationToken.None);
                _lastReverseGeocodeTick = Stopwatch.GetTimestamp();

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < maxRetries)
                    {
                        _logger.LogInformation("Reverse geocode rate-limited for ({Lat},{Lon}), retry {Attempt}/{Max}", lat, lon, attempt + 1, maxRetries);
                        await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                        continue;
                    }
                    _logger.LogWarning("Reverse geocode rate-limited after {Max} retries for ({Lat},{Lon})", maxRetries, lat, lon);
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Reverse geocode returned {Status} for ({Lat},{Lon})", response.StatusCode, lat, lon);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(CancellationToken.None);
                using var doc = JsonDocument.Parse(json);
                var address = doc.RootElement.GetProperty("address");

                if (address.TryGetProperty("city", out var city)) return city.GetString();
                if (address.TryGetProperty("municipality", out var municipality)) return municipality.GetString();
                if (address.TryGetProperty("town", out var town)) return town.GetString();
                if (address.TryGetProperty("county", out var county)) return county.GetString();
                if (address.TryGetProperty("village", out var village)) return village.GetString();

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reverse geocoding failed for ({Lat},{Lon})", lat, lon);
                return null;
            }
        }

        return null;
    }

    private static string? ExtractCityFromWikipedia(string wikipedia)
    {
        var parenIndex = wikipedia.IndexOf('(');
        if (parenIndex > 0)
        {
            var city = wikipedia[(parenIndex + 1)..];
            var closeParen = city.IndexOf(')');
            if (closeParen > 0)
                city = city[..closeParen];
            return city.Trim();
        }
        return null;
    }

    private static string? ExtractCityFromIsIn(string isIn)
    {
        var parts = isIn.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private static string NormalizeText(string text)
    {
        return text
            .ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c))
            .ToString()
            .Replace('\t', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();
    }

    private sealed record OsmBairro(
        string OsmId, string OsmType, string Name, string NormalizedName,
        string? PlaceType, string? Boundary, string? AdminLevel,
        double? Latitude, double? Longitude,
        string? OsmCityName = null);

    private sealed class ViaCepPayload
    {
        public string? Cep { get; init; }
        public string? Logradouro { get; init; }
        public string? Complemento { get; init; }
        public string? Bairro { get; init; }
        public string? Localidade { get; init; }
        public string? Uf { get; init; }
        public string? Ibge { get; init; }
        public string? Erro { get; init; }
    }

    private sealed class NominatimResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("osm_id")]
        public long OsmId { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("osm_type")]
        public string? OsmType { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("category")]
        public string? Category { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string? Type { get; init; }
    }

    private sealed class OverpassResponse
    {
        public List<OsmElement> Elements { get; init; } = new();
    }

    private sealed class OsmElement
    {
        public long Id { get; init; }
        public string? Type { get; init; }
        public double? Lat { get; init; }
        public double? Lon { get; init; }
        public OsmCenter? Center { get; init; }
        public Dictionary<string, string>? Tags { get; init; }
    }

    private sealed class OsmCenter
    {
        public double Lat { get; init; }
        public double Lon { get; init; }
    }
}
