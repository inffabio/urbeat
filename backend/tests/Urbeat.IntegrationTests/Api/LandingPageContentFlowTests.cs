using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.Dtos;
using Urbeat.IntegrationTests.Infrastructure;
using Urbeat.Application.DTOs;

namespace Urbeat.IntegrationTests.Api;

public sealed class LandingPageContentFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LandingPageContentFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdminCrudLandingPageContent_ShouldSucceed()
    {
        // Arrange
        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createRequest = new LandingPageContentRequestDto
        {
            Section = "Hero",
            Key = "Title",
            Value = "Seu delivery, com cara de restaurante.",
            DisplayOrder = 1,
            IsActive = true,
            Description = "Main hero title"
        };

        // Act - Create
        var createResponse = await adminClient.PostAsJsonAsync("/api/landingpagecontent", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<LandingPageContentResponseDto>();
        created.Should().NotBeNull();
        created!.Key.Should().Be("Title");

        // Act - Get All
        var getAllResponse = await adminClient.GetFromJsonAsync<List<LandingPageContentResponseDto>>("/api/landingpagecontent");
        getAllResponse.Should().NotBeNull();
        getAllResponse!.Should().Contain(x => x.Id == created.Id);

        // Act - Update
        var updateRequest = new LandingPageContentRequestDto
        {
            Section = "Hero",
            Key = "Title",
            Value = "Seu delivery profissional.",
            DisplayOrder = 1,
            IsActive = true,
            Description = "Updated title"
        };
        var updateResponse = await adminClient.PutAsJsonAsync($"/api/landingpagecontent/{created.Id}", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<LandingPageContentResponseDto>();
        updated!.Value.Should().Be("Seu delivery profissional.");

        // Act - Delete
        var deleteResponse = await adminClient.DeleteAsync($"/api/landingpagecontent/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Act - Verify Deleted
        var getDeletedResponse = await adminClient.GetAsync($"/api/landingpagecontent/{created.Id}");
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublicEndpoints_ShouldReturnOnlyActiveContent()
    {
        // Arrange
        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        await adminClient.PostAsJsonAsync("/api/landingpagecontent", new LandingPageContentRequestDto
        {
            Section = "Stats",
            Key = "StoreCount",
            Value = "1200",
            DisplayOrder = 1,
            IsActive = true
        });

        await adminClient.PostAsJsonAsync("/api/landingpagecontent", new LandingPageContentRequestDto
        {
            Section = "Stats",
            Key = "HiddenStat",
            Value = "0",
            DisplayOrder = 2,
            IsActive = false
        });

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var allResponse = await publicClient.GetFromJsonAsync<List<LandingPageContentResponseDto>>("/api/landingpagecontent");
        var sectionResponse = await publicClient.GetFromJsonAsync<List<LandingPageContentResponseDto>>("/api/landingpagecontent/section/Stats");

        // Assert
        allResponse.Should().NotBeNull();
        allResponse!.Should().NotContain(x => x.Key == "HiddenStat");
        
        sectionResponse.Should().NotBeNull();
        sectionResponse!.Should().ContainSingle(x => x.Key == "StoreCount");
    }

    [Fact]
    public async Task UnauthorizedUser_ShouldNotAccessAdminEndpoints()
    {
        // Arrange
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var sellerToken = await RegisterAndLoginSellerAsync(sellerClient);
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        // Act
        var createResponse = await sellerClient.PostAsJsonAsync("/api/landingpagecontent", new LandingPageContentRequestDto
        {
            Section = "Hero",
            Key = "Test",
            Value = "Test"
        });

        // Assert
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<string> LoginAdminAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/admin", new LoginRequestDto
        {
            Email = "admin@urbeat.local",
            Password = "Admin12345"
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();
        return token!.AccessToken;
    }

    private async Task<string> RegisterAndLoginSellerAsync(HttpClient client)
    {
        var email = $"lp.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "LP Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11987776666"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();
        return token!.AccessToken;
    }
}
