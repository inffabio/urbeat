using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence;

public sealed class CuisineTypeSeeder
{
    private static readonly string[] DefaultCuisineTypes =
    [
        "Hamburgueria",
        "Lanches",
        "Pizzaria",
        "Cachorro Quente",
        "Comida Japonesa",
        "Comida Árabe",
        "Açaiteria",
        "Tapioca e crepes"
    ];

    private readonly ApplicationDbContext _dbContext;

    public CuisineTypeSeeder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var hasData = await _dbContext.CuisineTypes.AnyAsync(cancellationToken);
        if (hasData)
        {
            return;
        }

        var cuisines = DefaultCuisineTypes
            .Select(name => new CuisineType
            {
                Name = name,
                IsActive = true
            });

        await _dbContext.CuisineTypes.AddRangeAsync(cuisines, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}