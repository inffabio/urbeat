using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreAdditionalsFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreAdditionalsFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldListGroupsFromProductOptionsAndCreateZeroPriceAdditional()
    {
        var client = await CreateAuthenticatedClientAsync();
        var (storeId, categoryId) = await CreateCategoryAsync(client);

        var productResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new
        {
            categoryId,
            name = "X-Burger",
            price = 25m,
            imageUrl = "https://example.com/x-burger.jpg",
            optionGroups = new[]
            {
                new { name = "Extras", isRequired = false, choiceType = "multiple", minChoices = 0, maxChoices = 3, displayOrder = 1, items = Array.Empty<object>() },
            },
        });
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var groupsResponse = await client.GetAsync($"/api/stores/{storeId}/additionals/groups");
        groupsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var groups = await groupsResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<StoreAdditionalGroupDto>>();
        groups.Should().ContainSingle(x => x.Name == "Extras");

        var group = groups!.Single(x => x.Name == "Extras");
        var createResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/additionals", new
        {
            name = "Molho especial",
            description = "Molho da casa",
            groupId = group.Id,
            price = 0m,
            isActive = true,
            displayOrder = 1,
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<StoreAdditionalDto>();
        created.Should().NotBeNull();
        created!.Price.Should().Be(0m);
        created.GroupName.Should().Be("Extras");
    }

    [Fact]
    public async Task Seller_ShouldToggleAndDeleteUnassignedAdditional()
    {
        var client = await CreateAuthenticatedClientAsync();
        var (storeId, categoryId) = await CreateCategoryAsync(client);
        var group = await CreateGroupAsync(client, storeId, categoryId);
        var created = await CreateAdditionalAsync(client, storeId, group.Id);

        var toggleResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/additionals/{created.Id}/status", new { isActive = false });
        toggleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await toggleResponse.Content.ReadFromJsonAsync<StoreAdditionalDto>())!.IsActive.Should().BeFalse();

        var deleteResponse = await client.DeleteAsync($"/api/stores/{storeId}/additionals/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await client.GetAsync($"/api/stores/{storeId}/additionals");
        var list = await listResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<StoreAdditionalDto>>();
        list.Should().NotContain(x => x.Id == created.Id);
    }

    [Fact]
    public async Task Seller_ShouldRejectDeletingAdditionalAssignedToProduct()
    {
        var client = await CreateAuthenticatedClientAsync();
        var (storeId, categoryId) = await CreateCategoryAsync(client);
        var group = await CreateGroupAsync(client, storeId, categoryId);
        var additional = await CreateAdditionalAsync(client, storeId, group.Id);

        var productResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new
        {
            categoryId,
            name = "Produto com adicional",
            price = 20m,
            imageUrl = "https://example.com/produto-adicional.jpg",
            additionalIds = new[] { additional.Id },
        });
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var deleteResponse = await client.DeleteAsync($"/api/stores/{storeId}/additionals/{additional.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Seller_ShouldRejectInvalidAdditionalRequest()
    {
        var client = await CreateAuthenticatedClientAsync();
        var (storeId, _) = await CreateCategoryAsync(client);

        var response = await client.PostAsJsonAsync($"/api/stores/{storeId}/additionals", new
        {
            name = "",
            groupId = Guid.Empty,
            price = -1m,
            displayOrder = -1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"additionals.flow.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Additionals",
            Email = email,
            Password = password,
            PhoneNumber = "11984443333",
        });
        await _factory.ConfirmEmailAsync(email);
        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto { Email = email, Password = password });
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private static async Task<(Guid StoreId, Guid CategoryId)> CreateCategoryAsync(HttpClient client)
    {
        var storeResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Adicionais",
            PhoneNumber = "11982221111",
            Description = "Loja para testes de adicionais",
            CuisineType = "Lanches",
            MaxDeliveryRadiusKm = 5,
        });
        var store = await storeResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        var categoryResponse = await client.PostAsJsonAsync($"/api/stores/{store!.Id}/categories", new { name = "Lanches", displayOrder = 1 });
        var category = await categoryResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();
        return (store.Id, category!.Id);
    }

    private static async Task<StoreAdditionalGroupDto> CreateGroupAsync(HttpClient client, Guid storeId, Guid categoryId)
    {
        var productResponse = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new
        {
            categoryId,
            name = "Produto grupo",
            price = 10m,
            imageUrl = "https://example.com/produto-grupo.jpg",
            optionGroups = new[] { new { name = "Extras", isRequired = false, choiceType = "multiple", minChoices = 0, maxChoices = 3, displayOrder = 1, items = Array.Empty<object>() } },
        });
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var groupsResponse = await client.GetAsync($"/api/stores/{storeId}/additionals/groups");
        var groups = await groupsResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<StoreAdditionalGroupDto>>();
        return groups!.Single(x => x.Name == "Extras");
    }

    private static async Task<StoreAdditionalDto> CreateAdditionalAsync(HttpClient client, Guid storeId, Guid groupId)
    {
        var response = await client.PostAsJsonAsync($"/api/stores/{storeId}/additionals", new { name = "Bacon", groupId, price = 5m, isActive = true, displayOrder = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StoreAdditionalDto>())!;
    }
}
