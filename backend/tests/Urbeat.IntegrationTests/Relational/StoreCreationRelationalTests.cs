using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// Stores.CuisineTypeId -> CuisineTypes and CuisineTypes.StoreId -> Stores are two independent
/// foreign keys. Creating a store together with a new private cuisine type makes both rows
/// "Added" at once, which EF cannot order and rejects with a circular-dependency exception.
/// Only a relational provider reproduces the batched INSERT path, so this test drives the exact
/// wizard payload against PostgreSQL. It is skipped when Docker is unavailable.
/// </summary>
public sealed class StoreCreationRelationalTests
{
    private readonly ITestOutputHelper _output;

    public StoreCreationRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldPersistStoreAndPrivateCuisine_ForWizardPayload()
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

            await using (var setupContext = new ApplicationDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
            }

            var mapper = new MapperConfiguration(configuration => configuration.AddProfile<EntityToDtoProfile>())
                .CreateMapper();
            var ownerUserId = Guid.NewGuid();
            Guid storeId;

            await using (var context = new ApplicationDbContext(options))
            {
                var service = new StoreService(
                    context,
                    mapper,
                    new UnusedStoreReadRepository(),
                    new EfUnitOfWork(context),
                    new FakeImageUploadService());

                var result = await service.CreateForOwnerAsync(ownerUserId, new CreateStoreRequestDto
                {
                    Name = "Loja Categoria Nova",
                    Slug = "loja-categoria-nova",
                    PhoneNumber = "11999999999",
                    CuisineType = "Comida Vegana",
                    SupportsDelivery = true,
                    SupportsPickup = true,
                    InitialMinute = 600,
                    FinalMinute = 1380,
                    MaxDeliveryRadiusKm = 5
                }, ipAddress: null);

                result.Created.Should().BeTrue();
                result.Store.Should().NotBeNull();
                storeId = result.Store!.Id;
            }

            await using (var verifyContext = new ApplicationDbContext(options))
            {
                var store = await verifyContext.Stores.SingleAsync(x => x.Id == storeId);
                var cuisine = await verifyContext.CuisineTypes.SingleAsync(x => x.StoreId == storeId);

                cuisine.Name.Should().Be("Comida Vegana");
                cuisine.NormalizedName.Should().Be("comida vegana");
                cuisine.IsDefault.Should().BeFalse();
                store.CuisineTypeId.Should().Be(cuisine.Id);

                (await verifyContext.CuisineTypes.CountAsync(x => x.NormalizedName == "comida vegana"))
                    .Should().Be(1);
                (await verifyContext.Stores.CountAsync(x => x.OwnerUserId == ownerUserId))
                    .Should().Be(1);
                (await verifyContext.AuditLogs.CountAsync(x => x.EntityId == storeId && x.Event == "StoreCreated"))
                    .Should().Be(1);
            }
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private sealed class UnusedStoreReadRepository : IStoreReadRepository
    {
        public Task<StoreResponseDto?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<StorePublicListItemDto>> ListPublicAsync(
            string? cuisineType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorePublicDetailsDto?> GetPublicByIdAsync(Guid storeId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorePublicDetailsDto?> GetPublicBySlugAsync(string slug, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorePublicDetailsDto?> GetPublicByPathAsync(string storePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

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
