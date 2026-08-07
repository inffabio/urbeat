using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class SubscriptionEnforcementFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SubscriptionEnforcementFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OverdueSubscription_ShouldBlockStore_FromPublicAndCheckout()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, sellerUserId, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 4m,
            MinimumOrderValue = 10m
        });

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza", 20m);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var upsertStatusResponse = await adminClient.PostAsJsonAsync("/api/admin/subscriptions/status", new UpsertSellerSubscriptionStatusRequestDto
        {
            SellerUserId = sellerUserId,
            NextDueDateUtc = DateTime.UtcNow.AddDays(-2),
            BillingStatus = SellerSubscriptionBillingStatus.Overdue
        });

        upsertStatusResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var processResponse = await adminClient.PostAsync("/api/admin/subscriptions/notifications/process", null);
        processResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var publicList = await publicClient.GetFromJsonAsync<List<StorePublicListItemDto>>("/api/public/stores");
        publicList.Should().NotBeNull();
        publicList!.Should().NotContain(x => x.Id == storeId);

        var publicDetailsResponse = await publicClient.GetAsync($"/api/public/stores/{storeId}");
        publicDetailsResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var reopenResponse = await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        reopenResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "10",
            Street = "Rua Bloqueio",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        address.Should().NotBeNull();

        var checkoutResponse = await customerClient.PostAsJsonAsync("/api/checkout/preview", new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id,
            PaymentMethod = PaymentMethod.PixOnline,
            Items =
            [
                new CheckoutItemRequestDto
                {
                    ProductId = productId,
                    Quantity = 1
                }
            ]
        });

        checkoutResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SellerSubscriptionEndpoint_ShouldReturnCurrentStatusForSeller()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, sellerUserId, _) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Burger");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var upsertStatusResponse = await adminClient.PostAsJsonAsync("/api/admin/subscriptions/status", new UpsertSellerSubscriptionStatusRequestDto
        {
            SellerUserId = sellerUserId,
            NextDueDateUtc = DateTime.UtcNow.AddDays(10),
            BillingStatus = SellerSubscriptionBillingStatus.Active
        });

        upsertStatusResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mySubscriptionResponse = await sellerClient.GetAsync("/api/subscriptions/my");
        mySubscriptionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await mySubscriptionResponse.Content.ReadFromJsonAsync<SellerSubscriptionMyResponseDto>();
        payload.Should().NotBeNull();
        payload!.HasSubscription.Should().BeTrue();
        payload.BillingStatus.Should().Be(SellerSubscriptionBillingStatus.Active);
        payload.LastChargeStatus.Should().Be("Pago");
        payload.StoreBlocked.Should().BeFalse();
        payload.RegularizationMessage.Should().NotBeNullOrWhiteSpace();
    }

    private async Task<(string AccessToken, Guid SellerUserId, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"subscription.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Subscription Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11982220000"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Subscription",
            PhoneNumber = "11987770000",
            Description = "Loja para assinatura",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        var sellerUserId = GetUserIdFromAccessToken(token.AccessToken);
        return (token.AccessToken, sellerUserId, store!.Id);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"subscription.customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Subscription Customer",
            Email = email,
            Password = password,
            PhoneNumber = "11981118888"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        return token!.AccessToken;
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

    private static Guid GetUserIdFromAccessToken(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var subject = jwt.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub)?.Value
            ?? jwt.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(subject, out var userId))
        {
            return userId;
        }

        throw new InvalidOperationException("Access token does not contain a valid seller user id.");
    }
}
