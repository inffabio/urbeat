using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Enums;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.Migrations;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// The InMemory provider cannot run the raw backfill SQL, so this test proves the relational path:
/// rows that only have the legacy organization flags are backfilled with the approved precedence
/// (<c>IsFeatured</c> &gt; <c>IsBestSeller</c> &gt; <c>IsNew</c>), the legacy columns and every
/// unrelated product field stay untouched, and the new column remains nullable. The test runs
/// against Testcontainers PostgreSQL and is skipped when Docker is unavailable.
/// </summary>
public sealed class ProductSpecialClassificationMigrationRelationalTests
{
    private readonly ITestOutputHelper _output;

    public ProductSpecialClassificationMigrationRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Migration_ShouldBackfillLegacyFlagsWithApprovedPrecedence_WithoutLosingData()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;

            Guid storeId;
            Guid categoryId;

            await using (var setupContext = new ApplicationDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();

                var store = new Store
                {
                    OwnerUserId = Guid.NewGuid(),
                    Name = "Loja Migracao",
                    Slug = "loja-migracao",
                    PhoneNumber = "11999999999",
                };
                var category = new ProductCategory
                {
                    StoreId = store.Id,
                    Name = "Lanches",
                    DisplayOrder = 1,
                    IsActive = true,
                };

                setupContext.Stores.Add(store);
                setupContext.ProductCategories.Add(category);

                setupContext.Products.AddRange(
                    LegacyProduct(store.Id, category.Id, "Todos os legados", isFeatured: true, isBestSeller: true, isNew: true, price: 10m),
                    LegacyProduct(store.Id, category.Id, "Mais vendido e novo", isFeatured: false, isBestSeller: true, isNew: true, price: 11m),
                    LegacyProduct(store.Id, category.Id, "Somente novo", isFeatured: false, isBestSeller: false, isNew: true, price: 12m),
                    LegacyProduct(store.Id, category.Id, "Sem classificacao", isFeatured: false, isBestSeller: false, isNew: false, price: 13m));

                await setupContext.SaveChangesAsync();

                storeId = store.Id;
                categoryId = category.Id;
            }

            await using (var migrationContext = new ApplicationDbContext(options))
            {
                // Simulate a database that stopped before this feature: the new column is absent
                // and every product still holds only the legacy flags.
                await migrationContext.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Products" DROP COLUMN "SpecialClassification";""");

                var generator = migrationContext.GetService<IMigrationsSqlGenerator>();
                var operations = new AddProductSpecialClassification().UpOperations;
                foreach (var command in generator.Generate(operations, migrationContext.Model))
                {
                    await migrationContext.Database.ExecuteSqlRawAsync(command.CommandText);
                }
            }

            await using (var verifyContext = new ApplicationDbContext(options))
            {
                var products = await verifyContext.Products
                    .Where(product => product.StoreId == storeId)
                    .OrderBy(product => product.Price)
                    .ToListAsync();

                products.Should().HaveCount(4);

                products[0].Name.Should().Be("Todos os legados");
                products[0].SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
                products[0].IsFeatured.Should().BeTrue();
                products[0].IsBestSeller.Should().BeTrue();
                products[0].IsNew.Should().BeTrue();

                products[1].Name.Should().Be("Mais vendido e novo");
                products[1].SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);
                products[1].IsFeatured.Should().BeFalse();
                products[1].IsBestSeller.Should().BeTrue();
                products[1].IsNew.Should().BeTrue();

                products[2].Name.Should().Be("Somente novo");
                products[2].SpecialClassification.Should().Be(ProductSpecialClassification.New);
                products[2].IsFeatured.Should().BeFalse();
                products[2].IsBestSeller.Should().BeFalse();
                products[2].IsNew.Should().BeTrue();

                products[3].Name.Should().Be("Sem classificacao");
                products[3].SpecialClassification.Should().BeNull();
                products[3].IsFeatured.Should().BeFalse();
                products[3].IsBestSeller.Should().BeFalse();
                products[3].IsNew.Should().BeFalse();

                // Unrelated fields survive the backfill.
                foreach (var product in products)
                {
                    product.StoreId.Should().Be(storeId);
                    product.CategoryId.Should().Be(categoryId);
                    product.ImageUrl.Should().Be($"https://example.com/{product.Price}.jpg");
                    product.Description.Should().Be("Produto legado");
                    product.IsAvailable.Should().BeTrue();
                }

                // The new contract accepts an explicit null and a defined value on the same row.
                products[0].SpecialClassification = null;
                await verifyContext.SaveChangesAsync();

                var cleared = await verifyContext.Products
                    .AsNoTracking()
                    .SingleAsync(product => product.Id == products[0].Id);
                cleared.SpecialClassification.Should().BeNull();
                cleared.IsFeatured.Should().BeTrue();
            }
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static Product LegacyProduct(
        Guid storeId,
        Guid categoryId,
        string name,
        bool isFeatured,
        bool isBestSeller,
        bool isNew,
        decimal price)
        => new()
        {
            StoreId = storeId,
            CategoryId = categoryId,
            Name = name,
            Description = "Produto legado",
            Price = price,
            ImageUrl = $"https://example.com/{price}.jpg",
            IsAvailable = true,
            DisplayOrder = 0,
            IsFeatured = isFeatured,
            IsBestSeller = isBestSeller,
            IsNew = isNew,
            SpecialClassification = null,
        };

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }
}
