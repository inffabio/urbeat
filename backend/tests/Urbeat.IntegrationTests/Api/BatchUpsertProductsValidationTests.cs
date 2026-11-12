using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// The batch endpoint replaces the whole catalog, so an invalid payload must be rejected
/// before any write. A null <c>items</c> collection used to reach the service and fail
/// mid-flight, after existing product images were already deleted.
/// </summary>
public sealed class BatchUpsertProductsValidationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BatchUpsertProductsValidationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BatchUpsert_ShouldReturn400AndKeepTheCatalog_WhenItemsIsNull()
    {
        var (client, storeId) = await SeedStoreAsync();
        var existingProductId = await ProductTestHelper.CreateProductAsync(client, storeId, "X-Bacon", 25.90m);

        var response = await client.PostAsJsonAsync(
            $"/api/stores/{storeId}/products/batch",
            new { items = (object?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var products = await client.GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>(
            $"/api/stores/{storeId}/products");
        products!.Select(x => x.Id).Should().Contain(existingProductId);
        products!.Should().HaveCount(1);
    }

    [Fact]
    public async Task BatchUpsert_ShouldReturn400AndKeepTheCatalog_WhenItemsIsMissing()
    {
        var (client, storeId) = await SeedStoreAsync();
        var existingProductId = await ProductTestHelper.CreateProductAsync(client, storeId, "X-Salada", 22.50m);

        var response = await client.PostAsJsonAsync($"/api/stores/{storeId}/products/batch", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var products = await client.GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>(
            $"/api/stores/{storeId}/products");
        products!.Select(x => x.Id).Should().Contain(existingProductId);
        products!.Should().HaveCount(1);
    }

    private async Task<(HttpClient Client, Guid StoreId)> SeedStoreAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"batch.validation.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await _factory.RegisterSellerAsync(client, email, password, "Seller Batch", "11984443333");

        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password,
        });
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var storeResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Batch",
            Slug = $"loja-batch-{Guid.NewGuid():N}",
            PhoneNumber = "11982221111",
            CuisineType = "Brasileira",
            MaxDeliveryRadiusKm = 5,
        });
        storeResponse.EnsureSuccessStatusCode();
        var store = await storeResponse.Content.ReadFromJsonAsync<StoreResponseDto>();

        return (client, store!.Id);
    }
}
