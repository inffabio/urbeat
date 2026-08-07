using Urbeat.Application.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Jobs;

public sealed class ImportStoreNeighborhoodsJob
{
    private readonly IOsmService _osmService;
    private readonly ILogger<ImportStoreNeighborhoodsJob> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;

    private const int MaxRetries = 3;

    public ImportStoreNeighborhoodsJob(
        IOsmService osmService,
        ILogger<ImportStoreNeighborhoodsJob> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        _osmService = osmService;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task ExecuteAsync(string city, string uf, Guid? storeId, int attempt = 0)
    {
        _logger.LogInformation(
            "ImportStoreNeighborhoodsJob started (attempt {Attempt}/{Max}) | City={City} Uf={Uf} StoreId={StoreId}",
            attempt + 1, MaxRetries, city, uf, storeId);

        try
        {
            var result = await _osmService.ImportNeighborhoodsByCityNameAsync(city, uf, storeId);

            _logger.LogInformation(
                "ImportStoreNeighborhoodsJob completed | City={City} Uf={Uf} Found={Found} Created={Created}",
                city, uf, result.Found, result.Created);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "ImportStoreNeighborhoodsJob skipped | City={City} Uf={Uf} | {Reason}",
                city, uf, ex.Message);
        }
        catch (Exception ex) when (attempt < MaxRetries - 1)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(4, attempt + 1));
            _logger.LogWarning(ex,
                "ImportStoreNeighborhoodsJob retry {NextAttempt}/{Max} in {Delay}s | City={City} Uf={Uf}",
                attempt + 2, MaxRetries, delay.TotalSeconds, city, uf);

            _backgroundJobClient.Schedule<ImportStoreNeighborhoodsJob>(
                job => job.ExecuteAsync(city, uf, storeId, attempt + 1),
                delay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ImportStoreNeighborhoodsJob exhausted retries | City={City} Uf={Uf} StoreId={StoreId} | {Reason}",
                city, uf, storeId, ex.Message);
        }
    }
}
