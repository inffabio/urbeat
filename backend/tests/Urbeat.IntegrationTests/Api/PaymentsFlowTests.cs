using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.IntegrationTests.Infrastructure;
using Urbeat.Infrastructure.Services.Payments;

namespace Urbeat.IntegrationTests.Api;

public sealed class PaymentsFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PaymentsFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Customer_ShouldStartAndTrackOnlinePayment_ForPendingOrder()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m } },
        });

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "111",
            Street = "Rua Pagamentos",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        address.Should().NotBeNull();

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Calabresa", 25m);

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
        var createdOrder = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        createdOrder.Should().NotBeNull();
        createdOrder!.Status.Should().Be(OrderStatus.PendingPayment);

        var paymentStartResponse = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = createdOrder.OrderId
        });

        paymentStartResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payment = await paymentStartResponse.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        payment.Should().NotBeNull();
        payment!.OrderId.Should().Be(createdOrder.OrderId);
        payment.Gateway.Should().Be(PaymentGateway.MercadoPago);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.GatewayTransactionId.Should().NotBeNullOrWhiteSpace();
        payment.GatewayCheckoutUrl.Should().NotBeNullOrWhiteSpace();

        var paymentTrackResponse = await customerClient.GetAsync($"/api/payments/order/{createdOrder.OrderId}");
        paymentTrackResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tracked = await paymentTrackResponse.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        tracked.Should().NotBeNull();
        tracked!.PaymentId.Should().Be(payment.PaymentId);
        tracked.History.Should().NotBeEmpty();
        tracked.History.Should().ContainSingle(x => x.NewStatus == PaymentStatus.Pending);

        var paymentHistoryResponse = await customerClient.GetAsync($"/api/payments/order/{createdOrder.OrderId}/history");
        paymentHistoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var paymentHistory = await paymentHistoryResponse.Content.ReadFromJsonAsync<List<PaymentStatusHistoryResponseDto>>();
        paymentHistory.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Customer_ShouldReturnSamePayment_WhenPaymentStartIsRetried()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m } },
        });

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "444",
            Street = "Rua Retry",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        address.Should().NotBeNull();

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Retry", 25m);

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
        var createdOrder = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        createdOrder.Should().NotBeNull();

        var firstStart = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = createdOrder!.OrderId
        });
        firstStart.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPayment = await firstStart.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        firstPayment.Should().NotBeNull();

        var retryStart = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = createdOrder.OrderId
        });
        retryStart.StatusCode.Should().Be(HttpStatusCode.OK);
        var retriedPayment = await retryStart.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        retriedPayment.Should().NotBeNull();

        retriedPayment!.PaymentId.Should().Be(firstPayment!.PaymentId);
        retriedPayment.GatewayTransactionId.Should().Be(firstPayment.GatewayTransactionId);
        retriedPayment.GatewayCheckoutUrl.Should().Be(firstPayment.GatewayCheckoutUrl);
    }

    [Fact]
    public async Task Customer_ShouldNotStartOnlinePayment_ForCashOrder()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Lanches");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m } },
        });

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "222",
            Street = "Rua Dinheiro",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        address.Should().NotBeNull();

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "X-Burguer", 20m);

        var createOrderResponse = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
        {
            StoreId = storeId, FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id,
            PaymentMethod = PaymentMethod.CashOnDelivery,
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
        var createdOrder = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        createdOrder.Should().NotBeNull();

        var paymentStartResponse = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = createdOrder!.OrderId
        });

        paymentStartResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var customerNotifications = await customerClient.GetFromJsonAsync<CustomerNotificationsResponseDto>("/api/customer/notifications");
        customerNotifications.Should().NotBeNull();
        customerNotifications!.UnreadCount.Should().Be(1);
        customerNotifications.Items.Should().ContainSingle(x => x.OrderId == createdOrder.OrderId && x.Type == NotificationType.OrderReceived);

        var sellerNotificationsResponse = await sellerClient.GetAsync("/api/seller/notifications");
        sellerNotificationsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sellerNotifications = await sellerNotificationsResponse.Content.ReadFromJsonAsync<SellerNotificationsResponseDto>();
        sellerNotifications.Should().NotBeNull();
        sellerNotifications!.UnreadCount.Should().Be(1);
        sellerNotifications.Items.Should().ContainSingle(x => x.OrderId == createdOrder.OrderId && x.Type == NotificationType.NewOrder);
    }

    [Fact]
    public async Task MercadoPagoWebhook_ShouldConfirmPayment_AndBeIdempotent()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m } },
        });

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "333",
            Street = "Rua Webhook",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        address.Should().NotBeNull();

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Portuguesa", 25m);

        var createOrderResponse = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
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
        });

        createOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        order.Should().NotBeNull();
        order!.Status.Should().Be(OrderStatus.PendingPayment);

        var startPaymentResponse = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = order.OrderId
        });

        startPaymentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payment = await startPaymentResponse.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        payment.Should().NotBeNull();

        var beforeWebhookStoreOrders = await sellerClient.GetFromJsonAsync<PagedOrderSummaryResponseDto>("/api/orders/store");
        beforeWebhookStoreOrders.Should().NotBeNull();
        beforeWebhookStoreOrders!.Items.Should().BeEmpty();

        var beforeWebhookCustomerNotifications = await customerClient.GetFromJsonAsync<CustomerNotificationsResponseDto>("/api/customer/notifications");
        beforeWebhookCustomerNotifications.Should().NotBeNull();
        beforeWebhookCustomerNotifications!.UnreadCount.Should().Be(0);

        var beforeWebhookNotifications = await sellerClient.GetFromJsonAsync<SellerNotificationsResponseDto>("/api/seller/notifications");
        beforeWebhookNotifications.Should().NotBeNull();
        beforeWebhookNotifications!.UnreadCount.Should().Be(0);

        var webhookPayload = $"{{\"type\":\"payment\",\"data\":{{\"id\":\"{payment!.GatewayTransactionId}\"}}}}";
        var webhookContent = new StringContent(webhookPayload, Encoding.UTF8, "application/json");
        var webhookClient = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var firstWebhookResponse = await webhookClient.PostAsync("/api/webhooks/mercadopago", webhookContent);
        firstWebhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondWebhookResponse = await webhookClient.PostAsync("/api/webhooks/mercadopago", new StringContent(webhookPayload, Encoding.UTF8, "application/json"));
        secondWebhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderDetails = await customerClient.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/{order.OrderId}");
        orderDetails.Should().NotBeNull();
        orderDetails!.Status.Should().Be(OrderStatus.Received);
        orderDetails.History.Count(x => x.NewStatus == OrderStatus.Received).Should().Be(1);

        var trackedPayment = await customerClient.GetFromJsonAsync<OrderPaymentResponseDto>($"/api/payments/order/{order.OrderId}");
        trackedPayment.Should().NotBeNull();
        trackedPayment!.Status.Should().Be(PaymentStatus.Paid);
        trackedPayment.History.Count(x => x.NewStatus == PaymentStatus.Paid).Should().Be(1);

        var afterWebhookStoreOrders = await sellerClient.GetFromJsonAsync<PagedOrderSummaryResponseDto>("/api/orders/store");
        afterWebhookStoreOrders.Should().NotBeNull();
        afterWebhookStoreOrders!.Items.Should().ContainSingle(x => x.Id == order.OrderId);

        var afterWebhookNotifications = await sellerClient.GetFromJsonAsync<SellerNotificationsResponseDto>("/api/seller/notifications");
        afterWebhookNotifications.Should().NotBeNull();
        afterWebhookNotifications!.UnreadCount.Should().Be(1);
        afterWebhookNotifications.Items.Should().ContainSingle(x => x.OrderId == order.OrderId && x.Type == NotificationType.NewOrder);

        var paymentHistoryResponse = await customerClient.GetAsync($"/api/payments/order/{order.OrderId}/history");
        paymentHistoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var paymentHistory = await paymentHistoryResponse.Content.ReadFromJsonAsync<List<PaymentStatusHistoryResponseDto>>();
        paymentHistory.Should().NotBeNull();
        paymentHistory!.Count(x => x.NewStatus == PaymentStatus.Paid).Should().Be(1);

        var customerNotifications = await customerClient.GetFromJsonAsync<CustomerNotificationsResponseDto>("/api/customer/notifications");
        customerNotifications.Should().NotBeNull();
        customerNotifications!.Items.Count(x => x.OrderId == order.OrderId && x.Type == NotificationType.OrderReceived).Should().Be(1);
    }

    [Fact]
    public async Task Customer_ShouldRetryPayment_AfterFailedWebhook_WithoutRecreatingOrder()
    {
        var adapterMock = new Mock<IMercadoPagoCheckoutAdapter>();
        var checkoutCalls = 0;
        adapterMock
            .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                checkoutCalls++;
                return new MercadoPagoCheckoutCreateResponse
                {
                    TransactionId = $"pref_{checkoutCalls}",
                    CheckoutUrl = $"https://checkout/{checkoutCalls}",
                    RawPayload = "{}"
                };
            });
        adapterMock
            .Setup(x => x.GetPaymentDetailsAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string transactionId, Guid? _, CancellationToken _) => new MercadoPagoPaymentDetails
            {
                TransactionId = transactionId,
                Status = "rejected",
                RawPayload = "{}"
            });

        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMercadoPagoCheckoutAdapter>();
            services.AddTransient<IMercadoPagoCheckoutAdapter>(_ => adapterMock.Object);
        }));

        var sellerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m } },
        });

        var customerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "555",
            Street = "Rua Retry Falha",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });

        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        address.Should().NotBeNull();

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Retry Falha", 25m);

        var createOrderResponse = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
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
        });

        createOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        order.Should().NotBeNull();
        order!.Status.Should().Be(OrderStatus.PendingPayment);

        var firstStart = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = order.OrderId
        });
        firstStart.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPayment = await firstStart.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        firstPayment.Should().NotBeNull();
        firstPayment!.GatewayTransactionId.Should().Be("pref_1");

        var webhookPayload = $"{{\"type\":\"payment\",\"data\":{{\"id\":\"pref_1\"}}}}";
        var webhookClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        var webhookResponse = await webhookClient.PostAsync("/api/webhooks/mercadopago", new StringContent(webhookPayload, Encoding.UTF8, "application/json"));
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var failedPayment = await customerClient.GetFromJsonAsync<OrderPaymentResponseDto>($"/api/payments/order/{order.OrderId}");
        failedPayment.Should().NotBeNull();
        failedPayment!.Status.Should().Be(PaymentStatus.Failed);

        var orderAfterFailure = await customerClient.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/{order.OrderId}");
        orderAfterFailure.Should().NotBeNull();
        orderAfterFailure!.Status.Should().Be(OrderStatus.PendingPayment);

        var retryStart = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = order.OrderId
        });
        retryStart.StatusCode.Should().Be(HttpStatusCode.OK);
        var retriedPayment = await retryStart.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        retriedPayment.Should().NotBeNull();

        retriedPayment!.PaymentId.Should().Be(firstPayment.PaymentId);
        retriedPayment.GatewayTransactionId.Should().Be("pref_2");
        retriedPayment.Status.Should().Be(PaymentStatus.Pending);

        var orderAfterRetry = await customerClient.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/{order.OrderId}");
        orderAfterRetry.Should().NotBeNull();
        orderAfterRetry!.Status.Should().Be(OrderStatus.PendingPayment);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"payments.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Payments Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11980000001"
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
            Name = "Loja Payments",
            Slug = "loja-payments",
            PhoneNumber = "11987770000",
            CuisineType = cuisineType
,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (token.AccessToken, store!.Id);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"payments.customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Payments Customer",
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
