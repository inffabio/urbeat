using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Persistence;

public sealed class DeliveryTimeSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DeliveryTimeSeeder> _logger;

    public DeliveryTimeSeeder(ApplicationDbContext dbContext, ILogger<DeliveryTimeSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        if (await _dbContext.Set<DeliveryTime>().AnyAsync())
        {
            _logger.LogInformation("DeliveryTimeSeeder: dados já existem — pulando.");
            return;
        }

        _logger.LogInformation("DeliveryTimeSeeder: populando tempos de entrega...");

        var deliveryTimes = new List<DeliveryTime>
        {
            new() { MinTimeMinutes = 15, MaxTimeMinutes = 25, IsActive = true },
            new() { MinTimeMinutes = 25, MaxTimeMinutes = 35, IsActive = true },
            new() { MinTimeMinutes = 30, MaxTimeMinutes = 40, IsActive = true },
            new() { MinTimeMinutes = 40, MaxTimeMinutes = 50, IsActive = true },
            new() { MinTimeMinutes = 50, MaxTimeMinutes = 60, IsActive = true },
        };

        await _dbContext.Set<DeliveryTime>().AddRangeAsync(deliveryTimes);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("DeliveryTimeSeeder: concluído.");
    }
}
