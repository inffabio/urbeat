using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Data;

public sealed class GooglePlacesTextSearchImporter
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<GooglePlacesTextSearchImporter> _logger;
    private readonly string? _apiKey;
    private static readonly HttpClient _http = new();

    public GooglePlacesTextSearchImporter(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ILogger<GooglePlacesTextSearchImporter> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _apiKey = configuration["GOOGLE_PLACES_API_KEY"];
    }

    public async Task ImportAsync(string uf, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("GooglePlacesTextSearch: no API key");
            return;
        }

        var cities = await _dbContext.Cities
            .Where(c => c.Uf == uf)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        int imported = 0;

        foreach (var city in cities)
        {
            try
            {
                // Check if city already has enough neighborhoods from IBGE
                var existingCount = await _dbContext.DeliveryNeighborhoods
                    .CountAsync(x => x.City == city.Name && x.IsActive, ct);
                if (existingCount > 10) continue; // skip if already has good coverage

                var count = await ImportCityNeighborhoodsAsync(city, ct);
                imported += count;
                if (count > 0)
                    _logger.LogInformation("GoogleTextSearch: {City} +{Count} bairros", city.Name, count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GoogleTextSearch failed: {City}", city.Name);
            }
            await Task.Delay(100, ct);
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("GoogleTextSearch done: {Imported} new bairros for {Uf}", imported, uf);
    }

    private async Task<int> ImportCityNeighborhoodsAsync(City city, CancellationToken ct)
    {
        var geoUrl = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(city.Name)},{city.Uf},Brasil&region=br&key={_apiKey}";
        var geoResp = await _http.GetFromJsonAsync<GeocodeResponse>(geoUrl, ct);
        var viewport = geoResp?.results?.FirstOrDefault()?.geometry?.viewport;
        if (viewport == null) return 0;

        var (rows, cols) = GetGridDensity(viewport);

        var allPlaces = new List<PlaceResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var latStep = (viewport.northeast.lat - viewport.southwest.lat) / rows;
        var lngStep = (viewport.northeast.lng - viewport.southwest.lng) / cols;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var cellSwLat = viewport.southwest.lat + r * latStep;
                var cellSwLng = viewport.southwest.lng + c * lngStep;
                var cellNeLat = cellSwLat + latStep;
                var cellNeLng = cellSwLng + lngStep;

                var cellPlaces = await SearchCellAsync(city.Name, cellSwLat, cellSwLng, cellNeLat, cellNeLng, ct);

                foreach (var place in cellPlaces)
                {
                    var name = place.displayName?.text?.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!seen.Add(name)) continue;
                    allPlaces.Add(place);
                }

                if (r < rows - 1 || c < cols - 1)
                    await Task.Delay(150, ct);
            }
        }

        _logger.LogInformation("GoogleTextSearch grid {Rows}x{Cols}: {City} found {Count} unique bairros",
            rows, cols, city.Name, allPlaces.Count);

        int imported = 0;

        foreach (var place in allPlaces)
        {
            var name = place.displayName?.text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var exists = await _dbContext.DeliveryNeighborhoods
                .AnyAsync(x => x.City == city.Name && x.NormalizedName == name.ToLowerInvariant(), ct);
            if (exists) continue;

            _dbContext.DeliveryNeighborhoods.Add(new DeliveryNeighborhood
            {
                CityId = city.Id,
                City = city.Name,
                Neighborhood = name,
                NormalizedName = name.ToLowerInvariant(),
                Latitude = place.location?.lat,
                Longitude = place.location?.lng,
                Source = "google_textsearch",
                IsActive = true
            });
            imported++;
        }

        return imported;
    }

    private static (int rows, int cols) GetGridDensity(GeocodeViewport viewport)
    {
        var latSpan = viewport.northeast.lat - viewport.southwest.lat;
        var lngSpan = viewport.northeast.lng - viewport.southwest.lng;
        var area = latSpan * lngSpan;

        if (area < 0.02) return (2, 2);
        if (area < 0.08) return (2, 3);
        if (area < 0.20) return (3, 4);
        return (4, 4);
    }

    private async Task<List<PlaceResult>> SearchCellAsync(
        string cityName,
        double swLat, double swLng,
        double neLat, double neLng,
        CancellationToken ct)
    {
        var payload = new
        {
            textQuery = $"bairros de {cityName}",
            includedType = "sublocality",
            locationRestriction = new
            {
                rectangle = new
                {
                    low = new { lat = swLat, lng = swLng },
                    high = new { lat = neLat, lng = neLng }
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://places.googleapis.com/v1/places:searchText");
        request.Headers.Add("X-Goog-Api-Key", _apiKey);
        request.Headers.Add("X-Goog-FieldMask", "places.displayName,places.types,places.formattedAddress,places.location");
        request.Content = content;

        var resp = await _http.SendAsync(request, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var result = await resp.Content.ReadFromJsonAsync<PlacesSearchResponse>(ct);
        return result?.places ?? [];
    }

    private sealed class GeocodeResponse
    {
        public List<GeocodeResult>? results { get; set; }
    }

    private sealed class GeocodeResult
    {
        public GeocodeGeometry? geometry { get; set; }
    }

    private sealed class GeocodeGeometry
    {
        public GeocodeViewport? viewport { get; set; }
    }

    private sealed class GeocodeViewport
    {
        public GeocodeCoord northeast { get; set; } = new();
        public GeocodeCoord southwest { get; set; } = new();
    }

    private sealed class GeocodeCoord
    {
        public double lat { get; set; }
        public double lng { get; set; }
    }

    private sealed class PlacesSearchResponse
    {
        public List<PlaceResult>? places { get; set; }
    }

    private sealed class PlaceResult
    {
        public PlaceDisplayName? displayName { get; set; }
        public PlaceLocation? location { get; set; }
    }

    private sealed class PlaceDisplayName
    {
        public string? text { get; set; }
    }

    private sealed class PlaceLocation
    {
        public double lat { get; set; }
        public double lng { get; set; }
    }
}
