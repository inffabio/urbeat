using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Jobs;

public sealed class ImportAllCitiesNeighborhoodsJob
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOsmService _osmService;
    private readonly ILogger<ImportAllCitiesNeighborhoodsJob> _logger;

    public ImportAllCitiesNeighborhoodsJob(
        ApplicationDbContext dbContext,
        IOsmService osmService,
        ILogger<ImportAllCitiesNeighborhoodsJob> logger)
    {
        _dbContext = dbContext;
        _osmService = osmService;
        _logger = logger;
    }

    public async Task ExecuteAsync(string? uf = null, int batchLimit = 10)
    {
        IQueryable<Domain.Entities.City> query = _dbContext.Set<Domain.Entities.City>()
            .AsNoTracking()
            .OrderBy(c => c.Name);

        if (!string.IsNullOrWhiteSpace(uf))
        {
            query = query.Where(c => c.Uf == uf.Trim().ToUpperInvariant());
        }

        var allCityIds = await query
            .Select(c => new { c.Id, c.Name, c.Uf })
            .ToListAsync();

        var alreadyCached = 0;
        var processed = 0;
        var failed = 0;

        foreach (var city in allCityIds)
        {
            if (processed >= batchLimit)
                break;

            try
            {
                var hasNeighborhoods = await _osmService.HasNeighborhoodsForCityAsync(city.Id);
                if (hasNeighborhoods)
                {
                    alreadyCached++;
                    continue;
                }

                _logger.LogInformation("Importing neighborhoods for {City}/{Uf}...", city.Name, city.Uf);
                var result = await _osmService.ImportNeighborhoodsByCityNameAsync(city.Name, city.Uf);

                if (result.Found > 0)
                {
                    processed++;
                    _logger.LogInformation("{City}: {Found} neighborhoods imported", city.Name, result.Found);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Failed to import neighborhoods for {City}/{Uf}",
                    city.Name, city.Uf);
            }
        }

        _logger.LogInformation(
            "Neighborhood import batch finished: {Cached} already cached, {Processed} imported, {Failed} failed (batch limit: {Limit})",
            alreadyCached, processed, failed, batchLimit);
    }
}
