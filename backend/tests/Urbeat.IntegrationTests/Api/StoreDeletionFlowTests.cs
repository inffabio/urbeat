using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreDeletionFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Issuer = "urbeat";
    private const string Audience = "urbeat-api";
    private const string Secret = "CHANGE_ME_MINIMUM_32_CHARS_SECRET";
    private const string CloudinaryBaseUrl = "https://res.cloudinary.com/urbeat/image/upload/v1712345678/";

    private readonly TestWebApplicationFactory _factory;

    public StoreDeletionFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteStore_ShouldRemoveStore_AttemptCloudinaryCleanup_AndAllowRecreation()
    {
        var (client, storeId, slug) = await CreateAuthenticatedSellerAsync();

        var response = await client.DeleteAsync($"/api/stores/{storeId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.ImageUploads.DeletedUrls.Should().Contain(new[]
        {
            CloudinaryBaseUrl + $"stores/{slug}/logo/logo.png",
            CloudinaryBaseUrl + $"stores/{slug}/banner/banner.png"
        });

        var myStoreResponse = await client.GetAsync("/api/stores/my-store");
        myStoreResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var recreateResponse = await client.PostAsJsonAsync("/api/stores", NewStoreRequest($"loja-{Guid.NewGuid():N}"));
        recreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondDelete = await client.DeleteAsync($"/api/stores/{storeId}");
        secondDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteStore_ShouldReturnForbidden_WhenSellerDoesNotOwnTheStore()
    {
        var (ownerClient, storeId, _) = await CreateAuthenticatedSellerAsync();
        var (otherClient, _, _) = await CreateAuthenticatedSellerAsync();

        var response = await otherClient.DeleteAsync($"/api/stores/{storeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ownerClient.GetAsync("/api/stores/my-store")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminCloudinaryEndpoints_ShouldBeAdminOnly_AndScanMustNotDelete()
    {
        var (sellerClient, _, _) = await CreateAuthenticatedSellerAsync();
        var sellerResponse = await sellerClient.GetAsync("/api/admin/cloudinary/orphan-images");
        sellerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var publicId = $"stores/integration/logo/scan-{Guid.NewGuid():N}";
        _factory.CloudinaryCatalog.Resources.Add(new CloudinaryImageResourceDto(publicId, DateTime.UtcNow.AddDays(-3), "png"));

        var adminClient = CreateAdminClient();
        var scanResponse = await adminClient.GetAsync("/api/admin/cloudinary/orphan-images?minimumAgeMinutes=60");
        scanResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var scan = await scanResponse.Content.ReadFromJsonAsync<StoreImageOrphanScanResultDto>();
        scan.Should().NotBeNull();
        scan!.Orphans.Should().Contain(x => x.PublicId == publicId);
        scan.MinimumAgeMinutes.Should().Be(60);
        _factory.CloudinaryCatalog.DestroyedPublicIds.Should().NotContain(publicId);
    }

    [Fact]
    public async Task CleanupOrphanImages_ShouldRequireConfirmation()
    {
        var publicId = $"stores/integration/logo/unconfirmed-{Guid.NewGuid():N}";
        _factory.CloudinaryCatalog.Resources.Add(new CloudinaryImageResourceDto(publicId, DateTime.UtcNow.AddDays(-3), "png"));

        var adminClient = CreateAdminClient();
        var response = await adminClient.PostAsJsonAsync("/api/admin/cloudinary/orphan-images/cleanup", new
        {
            confirm = false,
            minimumAgeMinutes = 60
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.CloudinaryCatalog.DestroyedPublicIds.Should().NotContain(publicId);
    }

    [Fact]
    public async Task CleanupOrphanImages_ShouldDeleteConfirmedOrphanOnly()
    {
        var publicId = $"stores/integration/logo/cleanup-{Guid.NewGuid():N}";
        _factory.CloudinaryCatalog.Resources.Add(new CloudinaryImageResourceDto(publicId, DateTime.UtcNow.AddDays(-3), "png"));

        var adminClient = CreateAdminClient();
        var response = await adminClient.PostAsJsonAsync("/api/admin/cloudinary/orphan-images/cleanup", new
        {
            confirm = true,
            minimumAgeMinutes = 60,
            maximumDeletions = 100
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<StoreImageOrphanCleanupResultDto>();
        result.Should().NotBeNull();
        result!.DeletedPublicIds.Should().Contain(publicId);
        result.ConfirmationRequired.Should().BeFalse();
        _factory.CloudinaryCatalog.DestroyedPublicIds.Should().Contain(publicId);
    }

    private async Task<(HttpClient Client, Guid StoreId, string Slug)> CreateAuthenticatedSellerAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"store-delete.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await _factory.RegisterSellerAsync(client, email, password, "Seller Delete", "11984443333");

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var slug = $"loja-delete-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/stores", NewStoreRequest(slug));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());

        var store = await createResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (client, store!.Id, slug);
    }

    private static CreateStoreRequestDto NewStoreRequest(string slug) => new()
    {
        Name = $"Loja {slug}",
        Slug = slug,
        PhoneNumber = "11982221111",
        CuisineType = "Pizza",
        MaxDeliveryRadiusKm = 5,
        LogoUrl = CloudinaryBaseUrl + $"stores/{slug}/logo/logo.png",
        BannerUrl = CloudinaryBaseUrl + $"stores/{slug}/banner/banner.png"
    };

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildAdminToken());
        return client;
    }

    private static string BuildAdminToken()
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "admin@urbeat.local"),
            new(ClaimTypes.Role, "Admin")
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
