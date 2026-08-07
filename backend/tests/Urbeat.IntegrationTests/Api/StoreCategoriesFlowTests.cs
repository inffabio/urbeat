using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreCategoriesFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreCategoriesFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldCreateAndListCategories()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Brasileira");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var listBefore = await client.GetAsync($"/api/stores/{storeId}/categories");
        listBefore.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyPayload = await listBefore.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductCategoryResponseDto>>();
        emptyPayload.Should().NotBeNull();
        emptyPayload!.Should().BeEmpty();

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Bebidas",
            DisplayOrder = 1
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Bebidas");
        created.DisplayOrder.Should().Be(1);
        created.IsActive.Should().BeTrue();

        var listAfter = await client.GetAsync($"/api/stores/{storeId}/categories");
        listAfter.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await listAfter.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductCategoryResponseDto>>();
        payload.Should().NotBeNull();
        payload!.Should().HaveCount(1);
        payload.First().Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Seller_ShouldUpdateCategory()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Pizza");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Pizzas",
            DisplayOrder = 1
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        var updateResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}/categories/{created!.Id}", new UpdateProductCategoryRequestDto
        {
            Name = "Pizzas Especiais",
            DisplayOrder = 2,
            IsActive = true
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Pizzas Especiais");
        updated.DisplayOrder.Should().Be(2);

        var listResponse = await client.GetAsync($"/api/stores/{storeId}/categories");
        var categories = await listResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductCategoryResponseDto>>();
        categories!.Single(x => x.Id == created.Id).Name.Should().Be("Pizzas Especiais");
    }

    [Fact]
    public async Task Seller_ShouldDeleteCategory()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Japonesa");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Temakis",
            DisplayOrder = 1
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        var deleteResponse = await client.DeleteAsync($"/api/stores/{storeId}/categories/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await client.GetAsync($"/api/stores/{storeId}/categories");
        var categories = await listResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductCategoryResponseDto>>();
        categories.Should().BeEmpty();
    }

    [Fact]
    public async Task Seller_ShouldRejectDeletingCategoryWithProducts()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Brasileira");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var categoryResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Lanches",
            DisplayOrder = 1
        });
        var category = await categoryResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        var productResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = category!.Id,
            Name = "X-Burger",
            Price = 25m,
            ImageUrl = "https://example.com/x-burger.jpg",
            DisplayOrder = 1
        });
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var deleteResponse = await client.DeleteAsync($"/api/stores/{storeId}/categories/{category.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Seller_ShouldReturnNotFound_WhenDeletingNonExistentCategory()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Mexicana");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var deleteResponse = await client.DeleteAsync($"/api/stores/{storeId}/categories/{Guid.NewGuid()}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Seller_ShouldReturnBadRequest_WhenCreatingCategoryWithEmptyName()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Árabe");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/categories", new CreateProductCategoryRequestDto
        {
            Name = string.Empty,
            DisplayOrder = 0
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnauthenticatedRequest_ShouldReturnUnauthorized()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/stores/{Guid.NewGuid()}/categories");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"categories.flow.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Categories",
            Email = email,
            Password = password,
            PhoneNumber = "11984443333"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        var accessToken = token!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Categorias",
            PhoneNumber = "11982221111",
            Description = "Loja para teste de categorias",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (accessToken, store!.Id);
    }
}
