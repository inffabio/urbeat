using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class OrdersFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OrdersFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CustomerAndSeller_ShouldHandleOrdersLifecycle()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 4m,
            MinimumOrderValue = 10m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 4m } },
        });

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Broto", 20m);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "100",
            Street = "Rua Pedido",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        var createOrderResponse = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
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

        createOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        created.Should().NotBeNull();

        var myOrdersResponse = await customerClient.GetAsync("/api/orders/my");
        myOrdersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var myOrders = await myOrdersResponse.Content.ReadFromJsonAsync<List<OrderSummaryResponseDto>>();
        myOrders.Should().NotBeNullOrEmpty();

        var orderDetailsResponse = await customerClient.GetAsync($"/api/orders/{created!.OrderId}");
        orderDetailsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var orderDetails = await orderDetailsResponse.Content.ReadFromJsonAsync<OrderDetailsResponseDto>();
        orderDetails.Should().NotBeNull();
        orderDetails!.Status.Should().Be(OrderStatus.PendingPayment);
        orderDetails.History.Should().NotBeEmpty();

        var storeOrdersResponse = await sellerClient.GetAsync("/api/orders/store");
        storeOrdersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var storeOrders = await storeOrdersResponse.Content.ReadFromJsonAsync<PagedOrderSummaryResponseDto>();
        storeOrders.Should().NotBeNull();
        storeOrders!.Items.Should().BeEmpty();

        var updateStatusResponse = await sellerClient.PatchAsJsonAsync($"/api/orders/{created.OrderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Received,
            Notes = "Pagamento confirmado manualmente"
        });

        updateStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var customerNotifications = await customerClient.GetFromJsonAsync<CustomerNotificationsResponseDto>("/api/customer/notifications");
        customerNotifications.Should().NotBeNull();
        customerNotifications!.Items.Should().ContainSingle(x => x.OrderId == created.OrderId && x.Type == NotificationType.OrderReceived);

        var storeOrdersAfterPaymentResponse = await sellerClient.GetAsync("/api/orders/store");
        storeOrdersAfterPaymentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var storeOrdersAfterPayment = await storeOrdersAfterPaymentResponse.Content.ReadFromJsonAsync<PagedOrderSummaryResponseDto>();
        storeOrdersAfterPayment.Should().NotBeNull();
        storeOrdersAfterPayment!.Items.Should().NotBeNullOrEmpty();

        var invalidTransitionResponse = await sellerClient.PatchAsJsonAsync($"/api/orders/{created.OrderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Delivered
        });

        invalidTransitionResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SellerStoreHistory_ShouldFilterByStatus_AndPaginate()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Lanches");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 3m,
            MinimumOrderValue = 5m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 3m } },
        });

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "90",
            Street = "Rua Historico",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        address.Should().NotBeNull();

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Lanche", 20m);

        var firstOrder = await CreateOrderAsync(customerClient, storeId, address!.Id, productId, PaymentMethod.PixOnline);
        var secondOrder = await CreateOrderAsync(customerClient, storeId, address.Id, productId, PaymentMethod.PixOnline);
        var thirdOrder = await CreateOrderAsync(customerClient, storeId, address.Id, productId, PaymentMethod.CashOnDelivery);

        await sellerClient.PatchAsJsonAsync($"/api/orders/{firstOrder}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Received
        });

        var pendingResponse = await sellerClient.GetAsync("/api/orders/store?status=PendingPayment&page=1&pageSize=10");
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendingPayload = await pendingResponse.Content.ReadFromJsonAsync<PagedOrderSummaryResponseDto>();
        pendingPayload.Should().NotBeNull();
        pendingPayload!.Items.Should().BeEmpty();
        pendingPayload.TotalItems.Should().Be(0);

        var pageOneResponse = await sellerClient.GetAsync("/api/orders/store?page=1&pageSize=1");
        pageOneResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageOnePayload = await pageOneResponse.Content.ReadFromJsonAsync<PagedOrderSummaryResponseDto>();
        pageOnePayload.Should().NotBeNull();
        pageOnePayload!.Items.Should().HaveCount(1);
        pageOnePayload.TotalItems.Should().Be(2);
        pageOnePayload.TotalPages.Should().Be(2);

        var pageTwoResponse = await sellerClient.GetAsync("/api/orders/store?page=2&pageSize=1");
        pageTwoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageTwoPayload = await pageTwoResponse.Content.ReadFromJsonAsync<PagedOrderSummaryResponseDto>();
        pageTwoPayload.Should().NotBeNull();
        pageTwoPayload!.Items.Should().HaveCount(1);
    }

    private static async Task<Guid> CreateOrderAsync(
        HttpClient customerClient,
        Guid storeId,
        Guid addressId,
        Guid productId,
        PaymentMethod paymentMethod)
    {
        var response = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = addressId,
            PaymentMethod = paymentMethod,
            Items =
            [
                new CheckoutItemRequestDto
                {
                    ProductId = productId,
                    Quantity = 1
                }
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        payload.Should().NotBeNull();
        return payload!.OrderId;
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"orders.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Orders Seller",
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
            Name = "Loja Orders",
            PhoneNumber = "11987770000",
            Description = "Loja para pedidos",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (token.AccessToken, store!.Id);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"orders.customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Orders Customer",
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
}
