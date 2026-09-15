using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldCreateStore_AndGetMyStore()
    {
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var email = $"store.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Store",
            Email = email,
            Password = password,
            PhoneNumber = "11988887777"
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

        var cuisineTypesResponse = await client.GetAsync("/api/stores/cuisine-types");
        cuisineTypesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuisineTypes = await cuisineTypesResponse.Content.ReadFromJsonAsync<List<CuisineTypeResponseDto>>();
        cuisineTypes.Should().NotBeNullOrEmpty();
        cuisineTypes!.Any(x => x.Name == "Pizzaria").Should().BeTrue();
        cuisineTypes.Should().OnlyContain(x => x.IsDefault && x.StoreId == null);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            CuisineType = "Pizzaria"
,
            MaxDeliveryRadiusKm = 5,
        });

        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var myStoreResponse = await client.GetAsync("/api/stores/my-store");
        myStoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Seller_ShouldNotCreateSecondStore()
    {
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var email = $"store.unique.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Unique",
            Email = email,
            Password = password,
            PhoneNumber = "11988886666"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Primeira Loja",
            Slug = "primeira-loja",
            PhoneNumber = "11999990000",
            CuisineType = "Lanches",
            MaxDeliveryRadiusKm = 5,
        });

        var secondCreateResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Segunda Loja",
            Slug = "segunda-loja",
            PhoneNumber = "11999991111",
            CuisineType = "Pizzaria"
,
            MaxDeliveryRadiusKm = 5,
        });

        secondCreateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Seller_ShouldNotCreateStore_WithBlankCuisineType()
    {
        var client = await CreateAuthenticatedSellerAsync("store.blank-cuisine");

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Sem Culinaria",
            Slug = "loja-sem-culinaria",
            PhoneNumber = "11991112222",
            CuisineType = string.Empty
,
            MaxDeliveryRadiusKm = 5,
        });

        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Seller_ShouldListStoreScopedCuisineTypes_WithDefaultsAndPrivateCategories()
    {
        var client = await CreateAuthenticatedSellerAsync("store.cuisine.list");
        var storeId = await CreateStoreAndGetIdAsync(client, "cuisine-list", "Lanches");

        var defaults = await client.GetFromJsonAsync<List<CuisineTypeResponseDto>>($"/api/stores/{storeId}/cuisine-types");
        defaults.Should().NotBeNullOrEmpty();
        defaults!.Should().OnlyContain(x => x.IsDefault && x.StoreId == null);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/stores/{storeId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "Comida Baiana" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CuisineTypeResponseDto>();
        created!.IsDefault.Should().BeFalse();
        created.StoreId.Should().Be(storeId);

        var scoped = await client.GetFromJsonAsync<List<CuisineTypeResponseDto>>($"/api/stores/{storeId}/cuisine-types");
        scoped!.Any(x => x.Name == "Comida Baiana").Should().BeTrue();
    }

    [Fact]
    public async Task Seller_ShouldNotAccessAnotherStoreCuisineTypes()
    {
        var ownerClient = await CreateAuthenticatedSellerAsync("store.cuisine.owner");
        var storeId = await CreateStoreAndGetIdAsync(ownerClient, "cuisine-owner", "Lanches");

        var intruderClient = await CreateAuthenticatedSellerAsync("store.cuisine.intruder");
        await CreateStoreAndGetIdAsync(intruderClient, "cuisine-intruder", "Lanches");

        var getResponse = await intruderClient.GetAsync($"/api/stores/{storeId}/cuisine-types");
        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var postResponse = await intruderClient.PostAsJsonAsync(
            $"/api/stores/{storeId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "Comida Baiana" });
        postResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Seller_ShouldNotCreateDuplicateOrProtectedStoreCuisineType()
    {
        var client = await CreateAuthenticatedSellerAsync("store.cuisine.duplicate");
        var storeId = await CreateStoreAndGetIdAsync(client, "cuisine-duplicate", "Lanches");

        var first = await client.PostAsJsonAsync(
            $"/api/stores/{storeId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "Comida Baiana" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync(
            $"/api/stores/{storeId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "comida baiana" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var protectedName = await client.PostAsJsonAsync(
            $"/api/stores/{storeId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "Pizzaria" });
        protectedName.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var blank = await client.PostAsJsonAsync(
            $"/api/stores/{storeId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "   " });
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Seller_ShouldNotDeleteProtectedOrInUseStoreCuisineType()
    {
        var client = await CreateAuthenticatedSellerAsync("store.cuisine.delete");
        var storeId = await CreateStoreAndGetIdAsync(client, "cuisine-delete", "Comida Baiana");

        var scoped = await client.GetFromJsonAsync<List<CuisineTypeResponseDto>>($"/api/stores/{storeId}/cuisine-types");
        var protectedCategory = scoped!.Single(x => x.Name == "Lanches");
        var inUseCategory = scoped!.Single(x => x.Name == "Comida Baiana");

        var protectedDelete = await client.DeleteAsync($"/api/stores/{storeId}/cuisine-types/{protectedCategory.Id}");
        protectedDelete.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var inUseDelete = await client.DeleteAsync($"/api/stores/{storeId}/cuisine-types/{inUseCategory.Id}");
        inUseDelete.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Seller_ShouldDeleteUnusedCustomStoreCuisineType()
    {
        var client = await CreateAuthenticatedSellerAsync("store.cuisine.delete-ok");
        var storeId = await CreateStoreAndGetIdAsync(client, "cuisine-delete-ok", "Lanches");

        var created = await client.PostAsJsonAsync(
            $"/api/stores/{storeId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "Comida Baiana" });
        var category = await created.Content.ReadFromJsonAsync<CuisineTypeResponseDto>();

        var deleteResponse = await client.DeleteAsync($"/api/stores/{storeId}/cuisine-types/{category!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var scoped = await client.GetFromJsonAsync<List<CuisineTypeResponseDto>>($"/api/stores/{storeId}/cuisine-types");
        scoped!.Any(x => x.Id == category.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Seller_ShouldUpdateStoreWithOwnPrivateCategory_IgnoringCase()
    {
        var client = await CreateAuthenticatedSellerAsync("store.cuisine.update-own");
        var storeId = await CreateStoreAndGetIdAsync(client, "cuisine-update-own", "Lanches");

        var createResponse = await client.PostAsJsonAsync(
            $"/api/stores/{storeId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "Comida Baiana" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var updateResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}", new UpdateStoreRequestDto
        {
            Name = "Loja cuisine-update-own",
            Slug = "cuisine-update-own",
            PhoneNumber = "11999999999",
            CuisineType = "COMIDA BAIANA",
            MaxDeliveryRadiusKm = 5,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var store = await client.GetFromJsonAsync<StoreResponseDto>("/api/stores/my-store");
        store!.CuisineType.Should().Be("Comida Baiana");
    }

    [Fact]
    public async Task Seller_ShouldUpdateStoreWithGlobalDefault_IgnoringAccents()
    {
        var client = await CreateAuthenticatedSellerAsync("store.cuisine.update-global");
        var storeId = await CreateStoreAndGetIdAsync(client, "cuisine-update-global", "Lanches");

        var updateResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}", new UpdateStoreRequestDto
        {
            Name = "Loja cuisine-update-global",
            Slug = "cuisine-update-global",
            PhoneNumber = "11999999999",
            CuisineType = "comida arabe",
            MaxDeliveryRadiusKm = 5,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var store = await client.GetFromJsonAsync<StoreResponseDto>("/api/stores/my-store");
        store!.CuisineType.Should().Be("Comida Árabe");
    }

    [Fact]
    public async Task Seller_ShouldRejectCategoryOwnedByAnotherStore()
    {
        var ownerClient = await CreateAuthenticatedSellerAsync("store.cuisine.update-owner");
        var ownerStoreId = await CreateStoreAndGetIdAsync(ownerClient, "cuisine-update-owner", "Lanches");

        var otherClient = await CreateAuthenticatedSellerAsync("store.cuisine.update-other");
        var otherStoreId = await CreateStoreAndGetIdAsync(otherClient, "cuisine-update-other", "Lanches");

        var createResponse = await otherClient.PostAsJsonAsync(
            $"/api/stores/{otherStoreId}/cuisine-types",
            new CreateCuisineTypeRequestDto { Name = "Comida Baiana" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var updateResponse = await ownerClient.PutAsJsonAsync($"/api/stores/{ownerStoreId}", new UpdateStoreRequestDto
        {
            Name = "Loja cuisine-update-owner",
            Slug = "cuisine-update-owner",
            PhoneNumber = "11999999999",
            CuisineType = "Comida Baiana",
            MaxDeliveryRadiusKm = 5,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var store = await ownerClient.GetFromJsonAsync<StoreResponseDto>("/api/stores/my-store");
        store!.CuisineType.Should().Be("Lanches");
    }

    private async Task<HttpClient> CreateAuthenticatedSellerAsync(string prefix)
    {
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var email = $"{prefix}.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var phoneNumber = $"119{Guid.NewGuid():N}"[..11];

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Seller {prefix}",
            Email = email,
            Password = password,
            PhoneNumber = phoneNumber
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private static async Task<Guid> CreateStoreAndGetIdAsync(HttpClient client, string slug, string cuisineType)
    {
        var createResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = $"Loja {slug}",
            Slug = slug,
            PhoneNumber = "11999999999",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var store = await client.GetFromJsonAsync<StoreResponseDto>("/api/stores/my-store");
        return store!.Id;
    }
}
