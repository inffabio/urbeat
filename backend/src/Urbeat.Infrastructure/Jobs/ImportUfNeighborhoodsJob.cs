using Urbeat.Application.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Jobs;

public sealed class ImportUfNeighborhoodsJob
{
    private readonly IOsmService _osmService;
    private readonly ILogger<ImportUfNeighborhoodsJob> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;

    private const int MaxRetryPasses = 3;

    public ImportUfNeighborhoodsJob(
        IOsmService osmService,
        ILogger<ImportUfNeighborhoodsJob> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        _osmService = osmService;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task ExecuteAsync(string uf, string[] cities)
    {
        await ExecuteAsync(uf, cities, null);
    }

    public async Task ExecuteAsync(string uf, string[] cities, List<string>? failedFromPreviousPass)
    {
        if (failedFromPreviousPass is { Count: > 0 })
        {
            cities = failedFromPreviousPass.ToArray();
        }

        var processed = 0;
        var skipped = 0;
        var failed = new List<string>();

        foreach (var city in cities)
        {
            try
            {
                var result = await _osmService.ImportNeighborhoodsByCityNameAsync(city.Trim(), uf);
                if (result.Found > 0)
                {
                    processed++;
                    _logger.LogInformation("ImportUfNeighborhoodsJob: {City}/{Uf} — {Found} bairros", city, uf, result.Found);
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                failed.Add(city);
                _logger.LogWarning(ex, "ImportUfNeighborhoodsJob: {City}/{Uf} failed", city, uf);
            }
        }

        _logger.LogInformation(
            "ImportUfNeighborhoodsJob pass finished for {Uf}: {Processed} processed, {Skipped} skipped, {Failed} failed",
            uf, processed, skipped, failed.Count);

        if (failed.Count > 0)
        {
            var passCount = failedFromPreviousPass is { Count: > 0 }
                ? (cities.Length == failedFromPreviousPass.Count ? "retry" : "partial")
                : "1st";

            if (failedFromPreviousPass is { Count: > 0 } && failed.Count >= failedFromPreviousPass.Count)
            {
                _logger.LogWarning(
                    "ImportUfNeighborhoodsJob: no progress on retry, giving up on {Count} cities: {Cities}",
                    failed.Count, string.Join(", ", failed));
                return;
            }

            var delay = TimeSpan.FromMinutes(2);
            _logger.LogInformation(
                "ImportUfNeighborhoodsJob: scheduling retry for {Count} failed cities in {Delay}min",
                failed.Count, delay.TotalMinutes);

            _backgroundJobClient.Schedule<ImportUfNeighborhoodsJob>(
                job => job.ExecuteAsync(uf, failed.ToArray(), failed),
                delay);
        }
    }
}
