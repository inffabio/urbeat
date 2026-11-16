using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Application.Security;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// End-to-end contract of the delegated administrative store access: opaque one-time token,
/// header-scoped identity, delegated modules, blocked destructive actions, audit correlation and
/// rate limiting.
/// </summary>
public sealed class AdminStoreAccessFlowTests
{
    private const string AdminEmail = "admin@urbeat.local";
    private const string AdminPassword = "Admin12345";
    private const string ContextHeader = "X-Admin-Store-Context";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ContextLifecycle_ShouldRequireAdmin_AndExposeTheTokenOnlyOnce()
    {
        using var factory = new TestWebApplicationFactory();
        var anonymous = factory.CreateClient();
        var (storeId, ownerEmail, _) = await CreateStoreAsync(factory);

        // Anonymous and seller sessions cannot open a context.
        (await anonymous.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/access-context",
            new { reason = "suporte" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var sellerClient = factory.CreateClient();
        var sellerToken = (await LoginAsync(sellerClient, ownerEmail, "Lojista123!", "/api/auth/login/seller"))!;
        using (var sellerRequest = new HttpRequestMessage(
                   HttpMethod.Post,
                   $"/api/admin/stores/{storeId}/access-context"))
        {
            sellerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);
            sellerRequest.Content = JsonContent.Create(new { reason = "suporte" });
            (await sellerClient.SendAsync(sellerRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var adminClient = factory.CreateClient();
        var adminToken = (await LoginAsync(adminClient, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        var context = await OpenContextAsync(adminClient, adminToken, storeId, "Chamado de suporte #4821");
        context.Token.Should().NotBeNullOrWhiteSpace();
        context.StoreId.Should().Be(storeId);
        context.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromMinutes(2));

        // The metadata read needs the header and never returns the token again.
        (await SendAsync(adminClient, adminToken, HttpMethod.Get,
            $"/api/admin/stores/{storeId}/access-context")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var metadata = await SendAsync(adminClient, adminToken, HttpMethod.Get,
            $"/api/admin/stores/{storeId}/access-context", context.Token);
        metadata.StatusCode.Should().Be(HttpStatusCode.OK);
        var metadataBody = await metadata.Content.ReadAsStringAsync();
        metadataBody.Should().NotContain("\"token\"");
        metadataBody.Should().NotContain(context.Token);

        (await SendAsync(adminClient, adminToken, HttpMethod.Delete,
            $"/api/admin/stores/{storeId}/access-context", context.Token)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        // Idempotent close.
        (await SendAsync(adminClient, adminToken, HttpMethod.Delete,
            $"/api/admin/stores/{storeId}/access-context", context.Token)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var afterClose = await SendAsync(adminClient, adminToken, HttpMethod.Get,
            $"/api/admin/stores/{storeId}/access-context", context.Token);
        afterClose.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await afterClose.Content.ReadAsStringAsync()).Should().Contain(AdminStoreErrorCodes.ContextInvalid);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var granted = await db.AuditLogs.SingleAsync(audit => audit.Event == "AdminStoreAccessGranted");
        granted.Description.Should().NotContain(context.Token);
        granted.EntityId.Should().Be(storeId);
        (await db.AuditLogs.CountAsync(audit => audit.Event == "AdminStoreAccessRevoked")).Should().Be(1);

        var rejections = await db.AuditLogs
            .Where(audit => audit.Event == "AdminStoreAccessRejected")
            .ToListAsync();
        rejections.Should().NotBeEmpty("a context denial must be audited even without a header");
        rejections.Should().OnlyContain(audit => !audit.Description.Contains(context.Token));
    }

    [Fact]
    public async Task DelegatedHeader_FromASellerSession_ShouldBeRejected()
    {
        using var factory = new TestWebApplicationFactory();
        var (storeId, ownerEmail, _) = await CreateStoreAsync(factory);
        var sellerClient = factory.CreateClient();
        var sellerToken = (await LoginAsync(sellerClient, ownerEmail, "Lojista123!", "/api/auth/login/seller"))!;

        var response = await SendAsync(
            sellerClient,
            sellerToken,
            HttpMethod.Get,
            "/api/stores/my-store",
            contextToken: "any-token");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(AdminStoreErrorCodes.ContextNotAllowed);
    }

    [Fact]
    public async Task DelegatedHeader_WithoutAJwt_ShouldBeUnauthorized()
    {
        using var factory = new TestWebApplicationFactory();
        var anonymous = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/stores/my-store");
        request.Headers.TryAddWithoutValidation(ContextHeader, "any-token");

        (await anonymous.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ContextOfOneStore_ShouldNotReachAnotherStore()
    {
        using var factory = new TestWebApplicationFactory();
        var (storeA, _, _) = await CreateStoreAsync(factory);
        var (storeB, _, _) = await CreateStoreAsync(factory);

        var adminClient = factory.CreateClient();
        var adminToken = (await LoginAsync(adminClient, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;
        var context = await OpenContextAsync(adminClient, adminToken, storeA);

        var response = await SendAsync(
            adminClient,
            adminToken,
            HttpMethod.Get,
            $"/api/stores/{storeB}/address",
            context.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain(AdminStoreErrorCodes.ContextStoreMismatch);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AuditLogs.CountAsync(audit => audit.Event == "AdminStoreActionDenied")).Should().Be(1);
    }

    [Fact]
    public async Task AdminWithContext_ShouldUseTheStoreOfTheContext_AndEditDelegatedModules()
    {
        using var factory = new TestWebApplicationFactory();
        var (storeId, ownerEmail, storeName) = await CreateStoreAsync(factory);

        var adminClient = factory.CreateClient();
        var adminToken = (await LoginAsync(adminClient, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;
        var context = await OpenContextAsync(adminClient, adminToken, storeId);

        var myStore = await SendAsync(adminClient, adminToken, HttpMethod.Get, "/api/stores/my-store", context.Token);
        myStore.StatusCode.Should().Be(HttpStatusCode.OK);
        var store = (await myStore.Content.ReadFromJsonAsync<StoreResponseDto>(JsonOptions))!;
        store.Id.Should().Be(storeId);
        store.Name.Should().Be(storeName);

        (await SendAsync(adminClient, adminToken, HttpMethod.Put, $"/api/stores/{storeId}/address", context.Token,
            new
            {
                street = "Rua Administrativa",
                number = "42",
                neighborhood = "Centro",
                city = "Sao Paulo",
                state = "SP",
                zipCode = "01001000"
            })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(adminClient, adminToken, HttpMethod.Put, $"/api/stores/{storeId}/business-hours",
            context.Token, new
            {
                items = Enumerable.Range(0, 7).Select(day => new
                {
                    dayOfWeek = day,
                    isOpen = true,
                    shifts = new[] { new { startTime = "08:00", endTime = "22:00" } }
                }).ToArray()
            })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(adminClient, adminToken, HttpMethod.Patch, $"/api/stores/{storeId}/delivery-config",
            context.Token, new
            {
                deliveryFee = 7.5m,
                minimumOrderValue = 20m,
                maxDeliveryRadiusKm = 8,
                deliveryAreas = Array.Empty<object>()
            })).StatusCode.Should().Be(HttpStatusCode.OK);

        var category = await SendAsync(adminClient, adminToken, HttpMethod.Post,
            $"/api/stores/{storeId}/categories", context.Token, new { name = "Bebidas", displayOrder = 1 });
        category.StatusCode.Should().Be(HttpStatusCode.Created);
        var categoryBody = (await category.Content.ReadFromJsonAsync<ProductCategoryResponseDto>(JsonOptions))!;

        var product = await SendAsync(adminClient, adminToken, HttpMethod.Post,
            $"/api/stores/{storeId}/products", context.Token, new
            {
                categoryId = categoryBody.Id,
                name = "Suco natural",
                price = 9.9m,
                imageUrl = "https://example.com/suco.png",
                displayOrder = 1
            });
        product.StatusCode.Should().Be(HttpStatusCode.Created);

        (await SendAsync(adminClient, adminToken, HttpMethod.Get, $"/api/stores/{storeId}/additionals",
            context.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(adminClient, adminToken, HttpMethod.Get, "/api/orders/store", context.Token)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await SendAsync(adminClient, adminToken, HttpMethod.Get, "/api/subscriptions/my", context.Token)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await SendAsync(adminClient, adminToken, HttpMethod.Get,
            $"/api/stores/{storeId}/publish/summary", context.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Publishing still honours the existing requirements.
        (await SendAsync(adminClient, adminToken, HttpMethod.Post,
            $"/api/stores/{storeId}/publish", context.Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = await db.Users.SingleAsync(user => user.Email == ownerEmail);

        var executed = await db.AuditLogs
            .Where(audit => audit.Event == "AdminStoreActionExecuted")
            .ToListAsync();
        executed.Should().NotBeEmpty();
        executed.Should().OnlyContain(audit =>
            audit.ActorUserId != null
            && audit.SubjectUserId == owner.Id
            && audit.AdminStoreContextId == context.ContextId);

        var addressAudit = await db.AuditLogs.SingleAsync(audit => audit.Event == "StoreAddressUpserted");
        addressAudit.UserId.Should().NotBe(owner.Id);
        addressAudit.ActorUserId.Should().NotBeNull();
        addressAudit.SubjectUserId.Should().Be(owner.Id);
        addressAudit.AdminStoreContextId.Should().Be(context.ContextId);
    }

    [Fact]
    public async Task AdminWithContext_ShouldNotPerformSellerOnlyOrDestructiveActions()
    {
        using var factory = new TestWebApplicationFactory();
        var (storeId, ownerEmail, _) = await CreateStoreAsync(factory);

        var adminClient = factory.CreateClient();
        var adminToken = (await LoginAsync(adminClient, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;
        var context = await OpenContextAsync(adminClient, adminToken, storeId);

        var blocked = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Delete, $"/api/stores/{storeId}", null),
            (HttpMethod.Patch, $"/api/stores/{storeId}/status", new { isOpen = true }),
            (HttpMethod.Post, "/api/subscriptions/contract", new { planId = Guid.NewGuid() }),
            (HttpMethod.Post, "/api/neighborhoods/import-by-city", new { city = "Sao Paulo", uf = "SP" }),
            (HttpMethod.Get, "/api/orders/store/customers", null),
            (HttpMethod.Get, "/api/reviews/store", null),
            (HttpMethod.Get, "/api/seller/printing/jobs", null)
        };

        foreach (var action in blocked)
        {
            var response = await SendAsync(
                adminClient,
                adminToken,
                action.Method,
                action.Url,
                context.Token,
                action.Body);

            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                $"{action.Method} {action.Url} must stay closed for the administrator");

            if (action.Url.StartsWith("/api/stores/", StringComparison.Ordinal)
                || action.Url.StartsWith("/api/neighborhoods/", StringComparison.Ordinal))
            {
                // Delegable prefixes report the stable out-of-scope action code.
                (await response.Content.ReadAsStringAsync())
                    .Should().Contain(AdminStoreErrorCodes.ActionNotAllowed);
            }
        }

        // The seller profile of the owner is never addressable through the delegated context.
        (await SendAsync(adminClient, adminToken, HttpMethod.Get, "/api/seller/profile", context.Token))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The seller path remains intact.
        var sellerClient = factory.CreateClient();
        var sellerToken = (await LoginAsync(sellerClient, ownerEmail, "Lojista123!", "/api/auth/login/seller"))!;
        (await SendAsync(sellerClient, sellerToken, HttpMethod.Get, "/api/stores/my-store")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await SendAsync(sellerClient, sellerToken, HttpMethod.Patch,
            $"/api/stores/{storeId}/status", body: new { isOpen = true })).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminWithoutContext_ShouldBeDeniedOnStoreEndpoints()
    {
        using var factory = new TestWebApplicationFactory();
        var (storeId, _, _) = await CreateStoreAsync(factory);

        var adminClient = factory.CreateClient();
        var adminToken = (await LoginAsync(adminClient, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        foreach (var url in new[]
                 {
                     "/api/stores/my-store",
                     $"/api/stores/{storeId}/products",
                     $"/api/stores/{storeId}/categories",
                     $"/api/stores/{storeId}/additionals",
                     "/api/orders/store",
                     "/api/subscriptions/my"
                 })
        {
            var response = await SendAsync(adminClient, adminToken, HttpMethod.Get, url);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{url} requires a store context");
            (await response.Content.ReadAsStringAsync()).Should().Contain(AdminStoreErrorCodes.ContextInvalid);
        }
    }

    [Fact]
    public async Task OpeningContexts_ShouldBeRateLimited()
    {
        using var factory = new TestWebApplicationFactory(
            adminRecoveryEnabled: false,
            adminRecoveryKey: null,
            passwordRecoveryPermitLimit: 50,
            useFakeEmail: false,
            adminStoreContextPermitLimit: 1);
        var (storeId, _, _) = await CreateStoreAsync(factory);

        var adminClient = factory.CreateClient();
        var adminToken = (await LoginAsync(adminClient, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        (await SendAsync(adminClient, adminToken, HttpMethod.Post,
            $"/api/admin/stores/{storeId}/access-context", body: new { reason = "primeiro" })).StatusCode
            .Should().Be(HttpStatusCode.Created);

        (await SendAsync(adminClient, adminToken, HttpMethod.Post,
            $"/api/admin/stores/{storeId}/access-context", body: new { reason = "segundo" })).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static async Task<AdminStoreAccessContextDto> OpenContextAsync(
        HttpClient client,
        string adminToken,
        Guid storeId,
        string? reason = null)
    {
        var response = await SendAsync(
            client,
            adminToken,
            HttpMethod.Post,
            $"/api/admin/stores/{storeId}/access-context",
            body: new { reason });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AdminStoreAccessContextDto>(JsonOptions))!;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string token,
        HttpMethod method,
        string url,
        string? contextToken = null,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (contextToken is not null)
        {
            request.Headers.TryAddWithoutValidation(ContextHeader, contextToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<(Guid StoreId, string OwnerEmail, string StoreName)> CreateStoreAsync(
        TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"delegated.{Guid.NewGuid():N}@urbeat.local";
        var storeName = $"Loja Delegada {Guid.NewGuid():N}";

        await factory.RegisterSellerAsync(client, email, "Lojista123!", "Lojista Delegado");
        var token = (await LoginAsync(client, email, "Lojista123!", "/api/auth/login/seller"))!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/stores");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreateStoreRequestDto
        {
            Name = storeName,
            Slug = $"loja-delegada-{Guid.NewGuid():N}",
            PhoneNumber = "11982221111",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5
        });

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var store = (await response.Content.ReadFromJsonAsync<StoreResponseDto>(JsonOptions))!;
        return (store.Id, email, storeName);
    }

    private static async Task<string?> LoginAsync(
        HttpClient client,
        string email,
        string password,
        string path)
    {
        var response = await client.PostAsJsonAsync(path, new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "login must succeed for " + email);
        var payload = await response.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        return payload?.AccessToken;
    }
}
