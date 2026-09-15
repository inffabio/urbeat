using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreProductsOptionGroupTemplateTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreProductsOptionGroupTemplateTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldReuseOptionGroupTemplateAcrossProducts()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, categoryId) = await RegisterLoginCreateStoreAndCategoryAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1-4. Cria o produto A com um grupo autoral "Escolha um molho".
        var productA = await CreateProductAsync(client, storeId, categoryId, "Produto A", new[]
        {
            MolhoGroup(null),
        });
        productA.OptionGroups.Should().ContainSingle();
        productA.OptionGroups.Single().TemplateId.Should().NotBeNull();

        // O grupo salvo fica disponível para a loja.
        var templates = await ListTemplatesAsync(client, storeId);
        templates.Should().ContainSingle();
        var template = templates.Single();
        template.Name.Should().Be("Escolha um molho");
        template.ChoiceType.Should().Be("multiple");
        template.MinChoices.Should().Be(0);
        template.MaxChoices.Should().Be(2);
        template.Items.Should().HaveCount(2);
        template.Items.Select(i => i.Price).Should().BeEquivalentTo(new[] { 5.00m, 5.50m });

        // 5-8. Cria o produto B selecionando o template.
        var productB = await CreateProductAsync(client, storeId, categoryId, "Produto B", new[]
        {
            MolhoGroup(template.Id),
        });
        productB.OptionGroups.Should().ContainSingle();
        productB.OptionGroups.Single().TemplateId.Should().Be(template.Id);
        productB.OptionGroups.Single().Items.Should().HaveCount(2);

        // 9. Reutilizar não duplica o grupo salvo.
        (await ListTemplatesAsync(client, storeId)).Should().ContainSingle();

        // 10. Produto C sem selecionar o grupo não recebe adicionais.
        var productC = await CreateProductAsync(client, storeId, categoryId, "Produto C", Array.Empty<ProductOptionGroupDto>());
        productC.OptionGroups.Should().BeEmpty();
        (await ListTemplatesAsync(client, storeId)).Should().ContainSingle();

        // 12. Editar o produto B preserva as edições enviadas e mantém o vínculo
        // com o template selecionado; o template da loja permanece intacto.
        var updateResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}/products/{productB.Id}", new UpdateProductRequestDto
        {
            CategoryId = categoryId,
            Name = "Produto B editado",
            Description = "Desc",
            Price = 45m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[] { MolhoGroup(template.Id, "Escolha um molho premium") },
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedB = await updateResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        updatedB!.OptionGroups.Should().ContainSingle();
        updatedB.OptionGroups.Single().TemplateId.Should().Be(template.Id);
        updatedB.OptionGroups.Single().Name.Should().Be("Escolha um molho premium");

        var persistedTemplate = (await ListTemplatesAsync(client, storeId)).Single();
        persistedTemplate.Name.Should().Be("Escolha um molho");

        // 13. Desmarcar o grupo não apaga o template reutilizável.
        var deselectResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}/products/{productB.Id}", new UpdateProductRequestDto
        {
            CategoryId = categoryId,
            Name = "Produto B editado",
            Description = "Desc",
            Price = 45m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = Array.Empty<ProductOptionGroupDto>(),
        });
        deselectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deselectResponse.Content.ReadFromJsonAsync<ProductResponseDto>())!.OptionGroups.Should().BeEmpty();
        (await ListTemplatesAsync(client, storeId)).Should().ContainSingle();
    }

    [Fact]
    public async Task OptionGroupTemplates_ShouldBeScopedToStore()
    {
        var firstClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (firstToken, firstStoreId, firstCategoryId) = await RegisterLoginCreateStoreAndCategoryAsync(firstClient);
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstToken);
        await CreateProductAsync(firstClient, firstStoreId, firstCategoryId, "Produto A", new[] { MolhoGroup(null) });
        (await ListTemplatesAsync(firstClient, firstStoreId)).Should().ContainSingle();

        var secondClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (secondToken, secondStoreId, _) = await RegisterLoginCreateStoreAndCategoryAsync(secondClient);
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);

        (await ListTemplatesAsync(secondClient, secondStoreId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Anonymous_ShouldNotListOptionGroupTemplates()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/stores/{Guid.NewGuid()}/products/option-groups");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DuplicateTemplateIds_ShouldBeRejected()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, categoryId) = await RegisterLoginCreateStoreAndCategoryAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await CreateProductAsync(client, storeId, categoryId, "Produto A", new[] { MolhoGroup(null) });
        var template = (await ListTemplatesAsync(client, storeId)).Single();

        var response = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = categoryId,
            Name = "Produto Duplicado",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1,
            OptionGroups = new[] { MolhoGroup(template.Id), MolhoGroup(template.Id) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListTemplatesAsync(client, storeId)).Should().ContainSingle();
    }

    [Fact]
    public async Task AuthoredGroup_WithExistingTemplateName_ShouldCreateIndependentTemplate()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, categoryId) = await RegisterLoginCreateStoreAndCategoryAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await CreateProductAsync(client, storeId, categoryId, "Produto A", new[] { MolhoGroup(null) });
        var template = (await ListTemplatesAsync(client, storeId)).Single();

        var productB = await CreateProductAsync(client, storeId, categoryId, "Produto B", new[]
        {
            new ProductOptionGroupDto
            {
                Name = "Escolha um molho",
                ChoiceType = "multiple",
                MinChoices = 0,
                MaxChoices = 2,
                Items = new[] { new ProductOptionItemDto { Name = "Outro molho", Price = 9m } },
            },
        });

        productB.OptionGroups.Single().TemplateId.Should().NotBe(template.Id);
        productB.OptionGroups.Single().Items.Should().ContainSingle().Which.Name.Should().Be("Outro molho");
        (await ListTemplatesAsync(client, storeId)).Should().HaveCount(2);
    }

    private static ProductOptionGroupDto MolhoGroup(Guid? templateId, string name = "Escolha um molho") => new()
    {
        Name = name,
        ChoiceType = "multiple",
        MinChoices = 0,
        MaxChoices = 2,
        TemplateId = templateId,
        Items = new[]
        {
            new ProductOptionItemDto { Name = "Molho 1", Price = 5.00m, DisplayOrder = 1 },
            new ProductOptionItemDto { Name = "Molho 2", Price = 5.50m, DisplayOrder = 2 },
        },
    };

    private static async Task<ProductResponseDto> CreateProductAsync(
        HttpClient client, Guid storeId, Guid categoryId, string name, IReadOnlyCollection<ProductOptionGroupDto> groups)
    {
        var response = await client.PostAsJsonAsync($"/api/stores/{storeId}/products", new CreateProductRequestDto
        {
            CategoryId = categoryId,
            Name = name,
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1,
            OptionGroups = groups,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductResponseDto>())!;
    }

    private static async Task<IReadOnlyCollection<ProductOptionGroupTemplateDto>> ListTemplatesAsync(HttpClient client, Guid storeId)
    {
        var response = await client.GetAsync($"/api/stores/{storeId}/products/option-groups");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyCollection<ProductOptionGroupTemplateDto>>())!;
    }

    private async Task<(string AccessToken, Guid StoreId, Guid CategoryId)> RegisterLoginCreateStoreAndCategoryAsync(HttpClient client)
    {
        var email = $"option.templates.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Seller {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11984443333",
        });
        if (!registerResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Register failed ({(int)registerResponse.StatusCode}): {await registerResponse.Content.ReadAsStringAsync()}");
        }

        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password,
        });
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        var accessToken = token!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Opcoes",
            Slug = $"loja-opcoes-{Guid.NewGuid():N}",
            PhoneNumber = "11982221111",
            CuisineType = "Brasileira",
            MaxDeliveryRadiusKm = 5,
        });
        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();

        var createCategoryResponse = await client.PostAsJsonAsync($"/api/stores/{store!.Id}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Categoria Teste",
            DisplayOrder = 1,
        });
        var category = await createCategoryResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        return (accessToken, store.Id, category!.Id);
    }
}
