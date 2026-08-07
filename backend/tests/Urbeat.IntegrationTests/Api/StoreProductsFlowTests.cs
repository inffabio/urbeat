using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreProductsFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreProductsFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldCreateAndListProducts()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, categoryId) = await RegisterLoginCreateStoreAndCategoryAsync(client, "Brasileira");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var listBefore = await client.GetAsync($"/api/stores/{storeId}/products");
        listBefore.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyPayload = await listBefore.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>();
        emptyPayload.Should().NotBeNull();
        emptyPayload!.Should().BeEmpty();

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = categoryId,
            Name = "Pizza Calabresa",
            Description = "Pizza de calabresa com cebola",
            Price = 42.90m,
                ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Pizza Calabresa");
        created.Price.Should().Be(42.90m);
        created.IsAvailable.Should().BeTrue();

        var listAfter = await client.GetAsync($"/api/stores/{storeId}/products");
        listAfter.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await listAfter.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>();
        payload.Should().NotBeNull();
        payload!.Should().HaveCount(1);
        payload.First().Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Seller_ShouldUpdateProduct()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, categoryId) = await RegisterLoginCreateStoreAndCategoryAsync(client, "Brasileira");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = categoryId,
            Name = "Pizza Margherita",
            Price = 39.90m,
                ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponseDto>();

        var updateResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}/products/{created!.Id}", new UpdateProductRequestDto
        {
            CategoryId = categoryId,
            Name = "Pizza Margherita Premium",
            Description = "Mussarela de búfala e manjericão",
            Price = 55.00m,
                ImageUrl = "https://example.com/p.jpg",
            IsAvailable = true,
            DisplayOrder = 2
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Pizza Margherita Premium");
        updated.Price.Should().Be(55.00m);
        updated.DisplayOrder.Should().Be(2);
    }

    [Fact]
    public async Task Seller_ShouldUpdateProductAvailability()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, categoryId) = await RegisterLoginCreateStoreAndCategoryAsync(client, "Japonesa");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = categoryId,
            Name = "Temaki Salmão",
            Price = 28.00m,
                ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponseDto>();

        var patchResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/products/{created!.Id}/availability",
            new UpdateProductAvailabilityRequestDto { IsAvailable = false });
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await patchResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        updated.Should().NotBeNull();
        updated!.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Seller_ShouldDeleteProduct()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, categoryId) = await RegisterLoginCreateStoreAndCategoryAsync(client, "Mexicana");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = categoryId,
            Name = "Taco",
            Price = 18.50m,
                ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponseDto>();

        var deleteResponse = await client.DeleteAsync($"/api/stores/{storeId}/products/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await client.GetAsync($"/api/stores/{storeId}/products");
        var products = await listResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>();
        products.Should().BeEmpty();
    }

    [Fact]
    public async Task Seller_ShouldReturnBadRequest_WhenCreatingProductWithEmptyName()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, categoryId) = await RegisterLoginCreateStoreAndCategoryAsync(client, "Árabe");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = categoryId,
            Name = string.Empty,
            Price = 30m,
            DisplayOrder = 0
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<(string AccessToken, Guid StoreId, Guid CategoryId)> RegisterLoginCreateStoreAndCategoryAsync(HttpClient client, string cuisineType)
    {
        var email = $"products.flow.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Products",
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
            Name = "Loja Produtos",
            PhoneNumber = "11982221111",
            Description = "Loja para teste de produtos",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();

        var createCategoryResponse = await client.PostAsJsonAsync($"/api/stores/{store!.Id}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Categoria Teste",
            DisplayOrder = 1
        });
        var category = await createCategoryResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        return (accessToken, store.Id, category!.Id);
    }
}
