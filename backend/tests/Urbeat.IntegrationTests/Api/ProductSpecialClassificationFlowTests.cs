using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Enums;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// End-to-end coverage for the single product special classification: the seller writes
/// one nullable value, the public catalog exposes it and the legacy featured endpoint
/// represents Offers without removing the product from the general catalog.
/// </summary>
public sealed class ProductSpecialClassificationFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProductSpecialClassificationFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldCreateClassifyReplaceAndClearTheProduct()
    {
        var (client, storeId, category) = await SeedStoreAsync();

        var created = await CreateProductAsync(client, storeId, category, "X-Bacon", ProductSpecialClassification.Offers);
        created.SpecialClassification.Should().Be(ProductSpecialClassification.Offers);

        var updated = await UpdateProductAsync(client, storeId, created, ProductSpecialClassification.BestSeller);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var bestSeller = await updated.Content.ReadFromJsonAsync<ProductResponseDto>();
        bestSeller!.SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);
        bestSeller.CategoryId.Should().Be(category);
        bestSeller.Price.Should().Be(created.Price);
        bestSeller.ImageUrl.Should().Be(created.ImageUrl);

        var cleared = await UpdateProductAsync(client, storeId, created, null);
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cleared.Content.ReadFromJsonAsync<ProductResponseDto>())!
            .SpecialClassification.Should().BeNull();
    }

    [Fact]
    public async Task Seller_ShouldRejectUnknownClassificationValues()
    {
        var (client, storeId, category) = await SeedStoreAsync();

        var unknownString = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new
        {
            categoryId = category,
            name = "X-Bacon",
            description = "Bacon",
            price = 25.9m,
            imageUrl = "https://example.com/x-bacon.jpg",
            specialClassification = "Destaque",
        });
        unknownString.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unknownNumber = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new
        {
            categoryId = category,
            name = "X-Bacon",
            description = "Bacon",
            price = 25.9m,
            imageUrl = "https://example.com/x-bacon.jpg",
            specialClassification = 99,
        });
        unknownNumber.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var list = await client.GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>($"/api/stores/{storeId}/products");
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Seller_ShouldNotChangeClassificationFromAnotherStore()
    {
        var (ownerClient, storeId, category) = await SeedStoreAsync();
        var created = await CreateProductAsync(ownerClient, storeId, category, "X-Bacon", ProductSpecialClassification.Offers);

        var (intruderClient, _, _) = await SeedStoreAsync();
        var response = await UpdateProductAsync(intruderClient, storeId, created, ProductSpecialClassification.New);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var catalog = await _factory.CreateClient().GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>(
            $"/api/public/stores/{storeId}/catalog/products");
        catalog!.Single().SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
    }

    [Fact]
    public async Task PublicCatalog_ShouldExposeClassificationOnceAndKeepFeaturedAsOffers()
    {
        var (client, storeId, category) = await SeedStoreAsync();

        var offer = await CreateProductAsync(client, storeId, category, "X-Bacon", ProductSpecialClassification.Offers);
        var bestSeller = await CreateProductAsync(client, storeId, category, "Batata", ProductSpecialClassification.BestSeller);
        var novelty = await CreateProductAsync(client, storeId, category, "Brownie", ProductSpecialClassification.New);
        var unclassified = await CreateProductAsync(client, storeId, category, "X-Salada", null);

        var publicClient = _factory.CreateClient();

        var catalog = await publicClient.GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>(
            $"/api/public/stores/{storeId}/catalog/products");
        var catalogProducts = catalog!;
        catalogProducts.Select(x => x.Id).Should().BeEquivalentTo(new[] { offer.Id, bestSeller.Id, novelty.Id, unclassified.Id });
        catalogProducts.Select(x => x.Id).Should().OnlyHaveUniqueItems();
        catalogProducts.Single(x => x.Id == offer.Id).SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
        catalogProducts.Single(x => x.Id == bestSeller.Id).SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);
        catalogProducts.Single(x => x.Id == novelty.Id).SpecialClassification.Should().Be(ProductSpecialClassification.New);
        catalogProducts.Single(x => x.Id == unclassified.Id).SpecialClassification.Should().BeNull();

        // The public contract exposes the normalized classification as a stable string field.
        var rawCatalog = await publicClient.GetStringAsync($"/api/public/stores/{storeId}/catalog/products");
        using var document = JsonDocument.Parse(rawCatalog);
        var offerJson = document.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == offer.Id);
        offerJson.GetProperty("specialClassification").GetString().Should().Be("Offers");
        offerJson.TryGetProperty("isFeatured", out _).Should().BeFalse();

        var featured = await publicClient.GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>(
            $"/api/public/stores/{storeId}/catalog/products/featured");
        featured!.Select(x => x.Id).Should().Equal(offer.Id);

        // Removing the classification only removes the special appearance.
        await UpdateProductAsync(client, storeId, offer, null);

        var catalogAfter = await publicClient.GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>(
            $"/api/public/stores/{storeId}/catalog/products");
        catalogAfter!.Select(x => x.Id).Should().Contain(offer.Id);

        var featuredAfter = await publicClient.GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>(
            $"/api/public/stores/{storeId}/catalog/products/featured");
        featuredAfter!.Should().BeEmpty();
    }

    [Fact]
    public async Task Seller_ShouldCreateReplacePreserveAndClearTheBatchClassification()
    {
        var (client, storeId, category) = await SeedStoreAsync();

        var created = await BatchUpsertAsync(client, storeId, category, "X-Bacon", "Offers");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var createdProducts = await created.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>();
        createdProducts!.Single().SpecialClassification.Should().Be(ProductSpecialClassification.Offers);

        var replaced = await BatchUpsertAsync(client, storeId, category, "X-Bacon", "BestSeller");
        replaced.StatusCode.Should().Be(HttpStatusCode.Created, await replaced.Content.ReadAsStringAsync());
        (await replaced.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>())!
            .Single().SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);

        // A resync that sends the same classification preserves it.
        var preserved = await BatchUpsertAsync(client, storeId, category, "X-Bacon", "BestSeller");
        (await preserved.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>())!
            .Single().SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);

        // An explicit null clears the classification and the legacy flags stay false.
        var cleared = await BatchUpsertAsync(client, storeId, category, "X-Bacon", classification: null);
        cleared.StatusCode.Should().Be(HttpStatusCode.Created, await cleared.Content.ReadAsStringAsync());
        (await cleared.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>())!
            .Single().SpecialClassification.Should().BeNull();

        var raw = await client.GetStringAsync($"/api/stores/{storeId}/products");
        using var document = JsonDocument.Parse(raw);
        var productJson = document.RootElement.EnumerateArray().Single();
        productJson.TryGetProperty("specialClassification", out var classificationProperty).Should().BeTrue();
        classificationProperty.ValueKind.Should().Be(JsonValueKind.Null);
        productJson.TryGetProperty("isFeatured", out _).Should().BeFalse();
        productJson.TryGetProperty("isBestSeller", out _).Should().BeFalse();
        productJson.TryGetProperty("isNew", out _).Should().BeFalse();
        productJson.GetProperty("categoryId").GetGuid().Should().Be(category);
        productJson.GetProperty("imageUrl").GetString().Should().Be("https://example.com/x-bacon.jpg");
    }

    [Fact]
    public async Task Seller_ShouldRejectInvalidBatchClassificationValues()
    {
        var (client, storeId, category) = await SeedStoreAsync();

        var unknownNumber = await BatchUpsertAsync(client, storeId, category, "X-Bacon", classification: 99);
        unknownNumber.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unknownString = await client.PostAsJsonAsync($"/api/stores/{storeId}/products/batch", new
        {
            items = new[]
            {
                new
                {
                    categoryId = category,
                    name = "X-Bacon",
                    description = "Bacon",
                    price = 25.9m,
                    imageUrl = "https://example.com/x-bacon.jpg",
                    specialClassification = "Destaque",
                },
            },
        });
        unknownString.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var list = await client.GetFromJsonAsync<IReadOnlyCollection<ProductResponseDto>>($"/api/stores/{storeId}/products");
        list.Should().BeEmpty();
    }

    private static Task<HttpResponseMessage> BatchUpsertAsync(
        HttpClient client, Guid storeId, Guid categoryId, string name, object? classification)
    {
        return client.PostAsJsonAsync($"/api/stores/{storeId}/products/batch", new
        {
            items = new[]
            {
                new
                {
                    categoryId,
                    name,
                    description = "Produto do batch",
                    price = 25.9m,
                    imageUrl = "https://example.com/x-bacon.jpg",
                    isAvailable = true,
                    displayOrder = 1,
                    specialClassification = classification,
                },
            },
        });
    }

    private async Task<(HttpClient Client, Guid StoreId, Guid CategoryId)> SeedStoreAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"special.flow.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await _factory.RegisterSellerAsync(client, email, password, "Seller Special", "11984443333");

        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password,
        });
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var storeResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Classificacao",
            Slug = $"loja-classificacao-{Guid.NewGuid():N}",
            PhoneNumber = "11982221111",
            CuisineType = "Brasileira",
            MaxDeliveryRadiusKm = 5,
        });
        storeResponse.EnsureSuccessStatusCode();
        var store = await storeResponse.Content.ReadFromJsonAsync<StoreResponseDto>();

        var categoryResponse = await client.PostAsJsonAsync($"/api/stores/{store!.Id}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Lanches",
            DisplayOrder = 1,
            IsActive = true,
        });
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        return (client, store.Id, category!.Id);
    }

    private static async Task<ProductResponseDto> CreateProductAsync(
        HttpClient client, Guid storeId, Guid categoryId, string name, ProductSpecialClassification? classification)
    {
        var response = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = categoryId,
            Name = name,
            Description = "Produto de teste",
            Price = 25.90m,
            ImageUrl = $"https://example.com/{name.ToLowerInvariant()}.jpg",
            IsAvailable = true,
            DisplayOrder = 1,
            SpecialClassification = classification,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductResponseDto>())!;
    }

    private static Task<HttpResponseMessage> UpdateProductAsync(
        HttpClient client, Guid storeId, ProductResponseDto product, ProductSpecialClassification? classification)
    {
        return client.PutAsJsonAsync($"/api/stores/{storeId}/products/{product.Id}", new UpdateProductRequestDto
        {
            CategoryId = product.CategoryId,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            ImageUrl = product.ImageUrl,
            IsAvailable = true,
            DisplayOrder = product.DisplayOrder,
            SpecialClassification = classification,
        });
    }
}
