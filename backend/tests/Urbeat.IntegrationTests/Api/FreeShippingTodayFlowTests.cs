using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// Covers the "frete grátis hoje" daily promotion: it is enabled/disabled explicitly by the
/// seller, persisted as a local Sao Paulo calendar date, and only applies on that same day for
/// any delivery destination (never for pickup).
/// </summary>
public sealed class FreeShippingTodayFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FreeShippingTodayFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Enabling_ShouldPersistTodaySaoPauloDate_AndExposeTrue()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            FreeShippingToday = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StoreResponseDto>();
        payload!.FreeShippingToday.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == storeId);
        persisted.FreeShippingToday.Should().BeTrue();
        persisted.FreeShippingTodayDate.Should().Be(StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow));

        var myStore = await sellerClient.GetFromJsonAsync<StoreResponseDto>("/api/stores/my-store");
        myStore!.FreeShippingToday.Should().BeTrue();
    }

    [Fact]
    public async Task Disabling_ShouldClearPersistedDate_AndExposeFalse()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            FreeShippingToday = true
        });

        var response = await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            FreeShippingToday = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StoreResponseDto>();
        payload!.FreeShippingToday.Should().BeFalse();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == storeId);
        persisted.FreeShippingToday.Should().BeFalse();
        persisted.FreeShippingTodayDate.Should().BeNull();
    }

    [Fact]
    public async Task Preview_ShouldApplyDailyFreeShipping_ForDestinationOutsideConfiguredAreas()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 9m,
            MinimumOrderValue = 10m,
            FreeShippingToday = true,
            DeliveryAreas = new[]
            {
                new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 9m }
            }
        });

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Hoje", 30m);

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // "Jardins" is deliberately NOT part of the configured delivery areas.
        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000", Number = "1", Street = "R", Neighborhood = "Jardins", City = "Sao Paulo", State = "SP", IsPrimary = true
        });
        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        var request = new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id, PaymentMethod = PaymentMethod.PixOnline,
            Items = [ new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 } ]
        };

        var response = await customerClient.PostAsJsonAsync("/api/checkout/preview", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "daily free shipping must cover any destination, body: " + await response.Content.ReadAsStringAsync());

        var preview = await response.Content.ReadFromJsonAsync<CheckoutSummaryResponseDto>();
        preview!.DeliveryFee.Should().Be(0m);
        preview.FreeShippingApplied.Should().BeTrue();
        preview.Total.Should().Be(30m);
    }

    [Fact]
    public async Task Preview_ShouldNotApplyDailyFreeShipping_WhenStoredDateIsExpired()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 9m,
            MinimumOrderValue = 10m,
            FreeShippingToday = true,
            DeliveryAreas = new[]
            {
                new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 9m }
            }
        });

        // Simulate the promotion having been enabled yesterday.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var store = await db.Stores.SingleAsync(x => x.Id == storeId);
            store.FreeShippingToday = true;
            store.FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1);
            await db.SaveChangesAsync();
        }

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Ontem", 30m);

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000", Number = "2", Street = "R", Neighborhood = "Centro", City = "Sao Paulo", State = "SP", IsPrimary = true
        });
        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        var request = new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id, PaymentMethod = PaymentMethod.PixOnline,
            Items = [ new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 } ]
        };

        var response = await customerClient.PostAsJsonAsync("/api/checkout/preview", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "body: " + await response.Content.ReadAsStringAsync());

        var preview = await response.Content.ReadFromJsonAsync<CheckoutSummaryResponseDto>();
        preview!.DeliveryFee.Should().Be(9m);
        preview.FreeShippingApplied.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_Pickup_ShouldNotApplyDailyFreeShipping()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 9m,
            MinimumOrderValue = 10m,
            FreeShippingToday = true
        });

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Retirada", 30m);

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var request = new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.PickUp,
            PaymentMethod = PaymentMethod.PixOnline,
            Items = [ new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 } ]
        };

        var response = await customerClient.PostAsJsonAsync("/api/checkout/preview", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "body: " + await response.Content.ReadAsStringAsync());

        var preview = await response.Content.ReadFromJsonAsync<CheckoutSummaryResponseDto>();
        preview!.FreeShippingApplied.Should().BeFalse();
    }

    [Fact]
    public async Task PublicDetails_ShouldExposeEffectiveFalse_WhenDateExpired_AndNotLeakDate()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            FreeShippingToday = true
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var store = await db.Stores.SingleAsync(x => x.Id == storeId);
            store.FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1);
            await db.SaveChangesAsync();
        }

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await publicClient.GetAsync($"/api/public/stores/{storeId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().NotContain("freeShippingTodayDate");

        var details = await response.Content.ReadFromJsonAsync<StorePublicDetailsDto>();
        details!.FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task PublicList_ShouldExposeEffectiveFreeShippingToday_AndNotLeakDate()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            FreeShippingToday = true
        });

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var activeResponse = await publicClient.GetAsync("/api/public/stores");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await activeResponse.Content.ReadAsStringAsync()).Should().NotContain("freeShippingTodayDate");

        var activeList = await activeResponse.Content.ReadFromJsonAsync<List<StorePublicListItemDto>>();
        activeList!.Single(x => x.Id == storeId).FreeShippingToday.Should().BeTrue();

        // Simulate the promotion having been enabled yesterday; the list must report it as effectively off.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var store = await db.Stores.SingleAsync(x => x.Id == storeId);
            store.FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1);
            await db.SaveChangesAsync();
        }

        var expiredResponse = await publicClient.GetAsync("/api/public/stores");
        var expiredList = await expiredResponse.Content.ReadFromJsonAsync<List<StorePublicListItemDto>>();
        expiredList!.Single(x => x.Id == storeId).FreeShippingToday.Should().BeFalse();
    }

    private static string UniquePhoneNumber()
    {
        var suffix = (Guid.NewGuid().GetHashCode() & 0x7FFFFFFF) % 100_000_000;
        return "119" + suffix.ToString("D8");
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"free.shipping.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Seller Frete Hoje {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = UniquePhoneNumber()
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
            Name = "Loja Frete Hoje",
            Slug = $"loja-frete-hoje-{Guid.NewGuid():N}",
            PhoneNumber = "11982223333",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 10
        });
        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.Created, "body: " + await createStoreResponse.Content.ReadAsStringAsync());

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (token.AccessToken, store!.Id);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"free.shipping.customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = $"Customer Frete Hoje {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = UniquePhoneNumber()
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
}
