using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.IntegrationTests.Api;

public sealed class NeonighborhoodsDeliveryFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public NeonighborhoodsDeliveryFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNeighborhoodsByStore_ShouldReturnOnlyStoreCityNeighborhoods_WhenRadiusIsSet()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, storeId, storeCity) = await RegisterLoginCreateStoreWithAddress(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.DeliveryNeighborhoods.Add(new DeliveryNeighborhood
        {
            Neighborhood = "Vila Madalena",
            NormalizedName = "vila madalena",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5500,
            Longitude = -46.6900,
            IsActive = true,
            Source = "test"
        });
        db.DeliveryNeighborhoods.Add(new DeliveryNeighborhood
        {
            Neighborhood = "Ipanema",
            NormalizedName = "ipanema",
            City = "Rio de Janeiro",
            CityId = Guid.NewGuid(),
            Latitude = -22.9833,
            Longitude = -43.2167,
            IsActive = true,
            Source = "test"
        });
        await db.SaveChangesAsync();

        var response = await client.GetAsync($"/api/stores/delivery-neighborhoods-by-store?storeId={storeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var neighborhoods = await response.Content.ReadFromJsonAsync<List<DeliveryNeighborhoodResponseDto>>();
        neighborhoods.Should().NotBeNull();
        neighborhoods!.Should().HaveCount(1);
        neighborhoods[0].Neighborhood.Should().Be("Vila Madalena");
    }

    [Fact]
    public async Task GetNeighborhoodsByStore_ShouldRequireAuth()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/stores/delivery-neighborhoods-by-store?storeId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PreviewRadius_ShouldFilterNeighborhoods_AndSaveRadiusPersists()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, storeId, storeCity) = await RegisterLoginCreateStoreWithAddress(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.DeliveryNeighborhoods.Add(new DeliveryNeighborhood
            {
                Neighborhood = "Bela Vista",
                NormalizedName = "bela vista",
                City = storeCity,
                CityId = Guid.NewGuid(),
                Latitude = -23.5580,
                Longitude = -46.6420,
                IsActive = true,
                Source = "test"
            });
            db.DeliveryNeighborhoods.Add(new DeliveryNeighborhood
            {
                Neighborhood = "Vila Mariana",
                NormalizedName = "vila mariana",
                City = storeCity,
                CityId = Guid.NewGuid(),
                Latitude = -23.5505,
                Longitude = -46.5840,
                IsActive = true,
                Source = "test"
            });
            await db.SaveChangesAsync();
        }

        var previewResponse = await client.GetAsync($"/api/stores/delivery-neighborhoods-by-store?storeId={storeId}&radiusKm=3");

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await previewResponse.Content.ReadFromJsonAsync<List<DeliveryNeighborhoodResponseDto>>();
        preview.Should().NotBeNull();
        preview!.Should().Contain(x => x.Neighborhood == "Bela Vista");
        preview.Should().NotContain(x => x.Neighborhood == "Vila Mariana");

        var updateResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 20m,
            MaxDeliveryRadiusKm = 8
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var storeResponse = await client.GetAsync("/api/stores/my-store");
        storeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var store = await storeResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        store.Should().NotBeNull();
        store!.MaxDeliveryRadiusKm.Should().Be(8);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    public async Task PreviewRadius_ShouldReturnBadRequest_WhenInvalid(string radiusKm)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, storeId, _) = await RegisterLoginCreateStoreWithAddress(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync($"/api/stores/delivery-neighborhoods-by-store?storeId={storeId}&radiusKm={radiusKm}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PreviewRadius_ShouldReturnEmpty_WhenPositiveRadiusHasNoEligibleNeighborhoods()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, storeId, storeCity) = await RegisterLoginCreateStoreWithAddress(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.DeliveryNeighborhoods.Add(new DeliveryNeighborhood
            {
                Neighborhood = "Itaquera",
                NormalizedName = "itaquera",
                City = storeCity,
                CityId = Guid.NewGuid(),
                Latitude = -23.5400,
                Longitude = -46.4600,
                IsActive = true,
                Source = "test"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/stores/delivery-neighborhoods-by-store?storeId={storeId}&radiusKm=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var neighborhoods = await response.Content.ReadFromJsonAsync<List<DeliveryNeighborhoodResponseDto>>();
        neighborhoods.Should().NotBeNull();
        neighborhoods!.Should().BeEmpty();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var globalNeighborhoods = await db.DeliveryNeighborhoods
                .Where(x => x.City == storeCity)
                .Select(x => x.Neighborhood)
                .ToListAsync();
            globalNeighborhoods.Should().Contain("Itaquera");
        }
    }

    [Fact]
    public async Task UpdateDeliveryConfig_ShouldRejectNonPositiveRadius()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, storeId, _) = await RegisterLoginCreateStoreWithAddress(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 20m,
            MaxDeliveryRadiusKm = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateDeliveryConfig_ShouldReduceStoreAssociations_WithoutDeletingGlobalNeighborhoods()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, storeId, storeCity) = await RegisterLoginCreateStoreWithAddress(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.DeliveryNeighborhoods.AddRange(
                new DeliveryNeighborhood
                {
                    Neighborhood = "Centro",
                    NormalizedName = "centro",
                    City = storeCity,
                    CityId = Guid.NewGuid(),
                    Latitude = -23.5505,
                    Longitude = -46.6333,
                    IsActive = true,
                    Source = "test"
                },
                new DeliveryNeighborhood
                {
                    Neighborhood = "Bela Vista",
                    NormalizedName = "bela vista",
                    City = storeCity,
                    CityId = Guid.NewGuid(),
                    Latitude = -23.5580,
                    Longitude = -46.6420,
                    IsActive = true,
                    Source = "test"
                },
                new DeliveryNeighborhood
                {
                    Neighborhood = "Vila Mariana",
                    NormalizedName = "vila mariana",
                    City = storeCity,
                    CityId = Guid.NewGuid(),
                    Latitude = -23.5505,
                    Longitude = -46.5840,
                    IsActive = true,
                    Source = "test"
                });
            await db.SaveChangesAsync();
        }

        var fullResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 20m,
            MaxDeliveryRadiusKm = 10,
            DeliveryAreas = new[]
            {
                new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Bela Vista", DeliveryFee = 6m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Vila Mariana", DeliveryFee = 7m, IsActive = true }
            }
        });
        fullResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Simulates a stale/direct client that still submits the out-of-radius "Vila Mariana"
        // while reducing the radius to 3 km. The server must drop it instead of persisting it.
        var reducedResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 20m,
            MaxDeliveryRadiusKm = 3,
            DeliveryAreas = new[]
            {
                new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Bela Vista", DeliveryFee = 6m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Vila Mariana", DeliveryFee = 7m, IsActive = true }
            }
        });
        reducedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var storeAreas = await db.Set<StoreDeliveryArea>()
                .Where(x => x.StoreId == storeId)
                .Select(x => x.Neighborhood)
                .ToListAsync();
            storeAreas.Should().BeEquivalentTo(new[] { "Centro", "Bela Vista" });

            var globalNeighborhoods = await db.DeliveryNeighborhoods
                .Where(x => x.City == storeCity)
                .Select(x => x.Neighborhood)
                .ToListAsync();
            globalNeighborhoods.Should().BeEquivalentTo(new[] { "Centro", "Bela Vista", "Vila Mariana" });
        }
    }

    [Fact]
    public async Task UpdateDeliveryConfig_ShouldRetainStoreOnlyManualArea_WhileRemovingKnownOutOfRadiusArea()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, storeId, storeCity) = await RegisterLoginCreateStoreWithAddress(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.DeliveryNeighborhoods.AddRange(
                new DeliveryNeighborhood
                {
                    Neighborhood = "Centro",
                    NormalizedName = "centro",
                    City = storeCity,
                    CityId = Guid.NewGuid(),
                    Latitude = -23.5505,
                    Longitude = -46.6333,
                    IsActive = true,
                    Source = "test"
                },
                new DeliveryNeighborhood
                {
                    Neighborhood = "Bela Vista",
                    NormalizedName = "bela vista",
                    City = storeCity,
                    CityId = Guid.NewGuid(),
                    Latitude = -23.5580,
                    Longitude = -46.6420,
                    IsActive = true,
                    Source = "test"
                },
                new DeliveryNeighborhood
                {
                    Neighborhood = "Vila Mariana",
                    NormalizedName = "vila mariana",
                    City = storeCity,
                    CityId = Guid.NewGuid(),
                    Latitude = -23.5505,
                    Longitude = -46.5840,
                    IsActive = true,
                    Source = "test"
                });
            await db.SaveChangesAsync();
        }

        // "Bairro Manual" has no global DeliveryNeighborhood record, so it is a
        // store-only manual association and cannot be classified by the radius.
        var response = await client.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 20m,
            MaxDeliveryRadiusKm = 3,
            DeliveryAreas = new[]
            {
                new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Bela Vista", DeliveryFee = 6m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Vila Mariana", DeliveryFee = 7m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Bairro Manual", DeliveryFee = 4m, IsActive = true }
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var storeAreas = await db.Set<StoreDeliveryArea>()
                .Where(x => x.StoreId == storeId)
                .Select(x => x.Neighborhood)
                .ToListAsync();
            storeAreas.Should().BeEquivalentTo(new[] { "Centro", "Bela Vista", "Bairro Manual" });

            var globalNeighborhoods = await db.DeliveryNeighborhoods
                .Where(x => x.City == storeCity)
                .Select(x => x.Neighborhood)
                .ToListAsync();
            globalNeighborhoods.Should().BeEquivalentTo(new[] { "Centro", "Bela Vista", "Vila Mariana" });
        }
    }

    [Fact]
    public async Task ImportByCity_ShouldReturnBadRequest_WhenCityIsEmpty()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, _, _) = await RegisterLoginCreateStoreWithAddress(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsJsonAsync("/api/neighborhoods/import-by-city", new
        {
            city = "",
            uf = "SP"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportByCity_ShouldReturnBadRequest_WhenUfIsEmpty()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, _, _) = await RegisterLoginCreateStoreWithAddress(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsJsonAsync("/api/neighborhoods/import-by-city", new
        {
            city = "Sao Paulo",
            uf = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportByCity_ShouldRequireAuth()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/api/neighborhoods/import-by-city", new
        {
            city = "Sao Paulo",
            uf = "SP"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(string AccessToken, Guid StoreId, string City)> RegisterLoginCreateStoreWithAddress(HttpClient client)
    {
        var email = $"delivery.test.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Seller Delivery Test {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = $"119{Random.Shared.Next(10000000, 99999999)}"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Delivery Test",
            Slug = $"loja-delivery-test-{Guid.NewGuid():N}",
            PhoneNumber = "11983334444",
            CuisineType = "Lanches",
            MaxDeliveryRadiusKm = 10
        });
        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        store.Should().NotBeNull();

        var city = $"Sao Paulo {Guid.NewGuid():N}";

        var addressResponse = await client.PutAsJsonAsync($"/api/stores/{store!.Id}/address", new
        {
            street = "Rua Teste",
            number = "100",
            neighborhood = "Centro",
            city,
            state = "SP",
            zipCode = "01001000",
            latitude = -23.5505,
            longitude = -46.6333
        });
        addressResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        return (token.AccessToken, store.Id, city);
    }
}
