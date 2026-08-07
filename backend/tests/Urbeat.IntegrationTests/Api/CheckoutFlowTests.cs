using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class CheckoutFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CheckoutFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Customer_ShouldPreviewAndConfirmCheckout()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var openResponse = await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto
        {
            IsOpen = true
        });
        openResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deliveryConfigResponse = await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5.50m,
            MinimumOrderValue = 20m,
            DeliveryAreas = new[]
            {
                new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5.50m }
            }
        });
        deliveryConfigResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Grande", 15m);

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var createAddressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "123",
            Street = "Rua Checkout",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        createAddressResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var address = await createAddressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        address.Should().NotBeNull();

        var request = new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id,
            PaymentMethod = PaymentMethod.PixOnline,
            Notes = "Sem cebola",
            Items =
            [
                new CheckoutItemRequestDto
                {
                    ProductId = productId,
                    Quantity = 2
                }
            ]
        };

        var previewResponse = await customerClient.PostAsJsonAsync("/api/checkout/preview", request);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK, "Preview should be OK, body: " + await previewResponse.Content.ReadAsStringAsync());

        var preview = await previewResponse.Content.ReadFromJsonAsync<CheckoutSummaryResponseDto>();
        preview.Should().NotBeNull();
        preview!.Subtotal.Should().Be(30m);
        preview.DeliveryFee.Should().Be(5.50m);
        preview.Total.Should().Be(35.50m);

        var confirmResponse = await customerClient.PostAsJsonAsync("/api/checkout/confirm", request);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var confirmation = await confirmResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        confirmation.Should().NotBeNull();
        confirmation!.OrderId.Should().NotBe(Guid.Empty);
        confirmation.Total.Should().Be(35.50m);
    }

    [Fact]
    public async Task Preview_ShouldApplyFreeShipping_WhenSubtotalReachesThreshold()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 9m,
            MinimumOrderValue = 10m,
            FreeShippingThreshold = 50m,
            DeliveryAreas = new[]
            {
                new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 9m }
            }
        });

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza XL", 30m);

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000", Number = "1", Street = "R", Neighborhood = "Centro", City = "Sao Paulo", State = "SP", IsPrimary = true
        });
        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        // 1 unidade (30) < 50 → cobra frete regional 9
        var belowRequest = new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id, PaymentMethod = PaymentMethod.PixOnline,
            Items = [ new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 } ]
        };
        var belowPreview = await (await customerClient.PostAsJsonAsync("/api/checkout/preview", belowRequest)).Content.ReadFromJsonAsync<CheckoutSummaryResponseDto>();
        belowPreview!.DeliveryFee.Should().Be(9m);
        belowPreview.FreeShippingApplied.Should().BeFalse();
        belowPreview.FreeShippingThreshold.Should().Be(50m);

        // 2 unidades (60) >= 50 → frete grátis
        var freeRequest = new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address.Id, PaymentMethod = PaymentMethod.PixOnline,
            Items = [ new CheckoutItemRequestDto { ProductId = productId, Quantity = 2 } ]
        };
        var freePreview = await (await customerClient.PostAsJsonAsync("/api/checkout/preview", freeRequest)).Content.ReadFromJsonAsync<CheckoutSummaryResponseDto>();
        freePreview!.DeliveryFee.Should().Be(0m);
        freePreview.FreeShippingApplied.Should().BeTrue();
        freePreview.Total.Should().Be(60m);
    }

    [Fact]
    public async Task Customer_ShouldBeBlocked_WhenStoreIsClosed()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (_, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Lanches");

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "X-Burger", 30m);

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var createAddressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "456",
            Street = "Rua Fechada",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await createAddressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        var request = new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id,
            PaymentMethod = PaymentMethod.CardOnline,
            Items =
            [
                new CheckoutItemRequestDto
                {
                    ProductId = productId,
                    Quantity = 1
                }
            ]
        };

        var previewResponse = await customerClient.PostAsJsonAsync("/api/checkout/preview", request);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"checkout.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Checkout Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11982223333"
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
            Name = "Loja Checkout",
            PhoneNumber = "11987778888",
            Description = "Loja para testes de checkout",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        createStoreResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, "store creation must succeed in tests, body: " + await createStoreResponse.Content.ReadAsStringAsync());
        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (token.AccessToken, store!.Id);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"checkout.customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Checkout Customer",
            Email = email,
            Password = password,
            PhoneNumber = "11981110000"
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
