using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class PublicCatalogFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PublicCatalogFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Public_ShouldListCategoriesAndProducts()
    {
        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (storeId, _) = await SeedStoreWithProductsAsync(publicClient);

        var categoriesResponse = await publicClient.GetAsync($"/api/public/stores/{storeId}/catalog/categories");
        categoriesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = await categoriesResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductCategoryResponseDto>>();
        categories.Should().NotBeNull();
        categories!.Should().HaveCount(2);

        var productsResponse = await publicClient.GetAsync($"/api/public/stores/{storeId}/catalog/products");
        productsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var products = await productsResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>();
        products.Should().NotBeNull();
        products!.Select(p => p.Name).Should().Contain(new[] { "Pizza Calabresa", "Coca-Cola" });
    }

    [Fact]
    public async Task Public_ShouldOnlyReturnAvailableProducts()
    {
        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (storeId, sellerClient) = await SeedStoreWithProductsAsync(publicClient);

        var productResponse = await sellerClient.GetAsync($"/api/stores/{storeId}/products");
        var products = await productResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>();
        var productToDisable = products!.First();

        var patchResponse = await sellerClient.PatchAsJsonAsync(
            $"/api/stores/{storeId}/products/{productToDisable.Id}/availability",
            new UpdateProductAvailabilityRequestDto { IsAvailable = false });
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var catalogResponse = await publicClient.GetAsync($"/api/public/stores/{storeId}/catalog/products");
        var catalogProducts = await catalogResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>();
        catalogProducts.Should().NotBeNull();
        catalogProducts!.Should().AllSatisfy(p => p.IsAvailable.Should().BeTrue());
        catalogProducts.Should().HaveCount(1);
    }

    [Fact]
    public async Task Public_ShouldReturnEmptyList_WhenStoreHasNoCatalog()
    {
        var (publicClient, storeId) = await CreateStoreWithoutCatalogAsync();

        var categoriesResponse = await publicClient.GetAsync($"/api/public/stores/{storeId}/catalog/categories");
        var categories = await categoriesResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductCategoryResponseDto>>();
        categories.Should().BeEmpty();

        var productsResponse = await publicClient.GetAsync($"/api/public/stores/{storeId}/catalog/products");
        var products = await productsResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>();
        products.Should().BeEmpty();
    }

    [Fact]
    public async Task Public_ShouldReturnEmptyList_WhenStoreDoesNotExist()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/public/stores/{Guid.NewGuid()}/catalog/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductCategoryResponseDto>>();
        categories.Should().BeEmpty();
    }

    private async Task<(Guid StoreId, HttpClient SellerClient)> SeedStoreWithProductsAsync(HttpClient publicClient)
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndGetStoreAsync(sellerClient, "Brasileira");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var cat1 = await sellerClient.PostAsJsonAsync($"/api/stores/{storeId}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Pizzas",
            DisplayOrder = 1
        });
        var category1 = await cat1.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        var cat2 = await sellerClient.PostAsJsonAsync($"/api/stores/{storeId}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Bebidas",
            DisplayOrder = 2
        });
        var category2 = await cat2.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        await sellerClient.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = category1!.Id,
            Name = "Pizza Calabresa",
            Price = 42.90m,
                ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1
        });

        await sellerClient.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = category2!.Id,
            Name = "Coca-Cola",
            Price = 8.50m,
                ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1
        });

        return (storeId, sellerClient);
    }

    private async Task<(HttpClient PublicClient, Guid StoreId)> CreateStoreWithoutCatalogAsync()
    {
        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndGetStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (publicClient, storeId);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndGetStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"catalog.flow.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Catalog",
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
            Name = "Loja Catalogo",
            PhoneNumber = "11982221111",
            Description = "Loja para teste de catalogo",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (accessToken, store!.Id);
    }
}
