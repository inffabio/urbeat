using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.IntegrationTests.Infrastructure;
using Urbeat.WebApi.Hubs;

namespace Urbeat.IntegrationTests.Api;

public sealed class OrderStatusSignalRTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OrderStatusSignalRTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateOrderStatus_ShouldEmitOrderStatusUpdatedToCustomer()
    {
        var recording = GetCustomerRecordingHub();
        recording.Clear();

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

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza SignalR", 20m);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "10",
            Street = "Rua SignalR",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });
        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        var createOrderResponse = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
        {
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id,
            PaymentMethod = PaymentMethod.PixOnline,
            Items = [new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 }]
        });
        createOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        var orderId = created!.OrderId;

        var orderDetails = await customerClient.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/{orderId}");
        orderDetails!.Code.Should().StartWith("URB-");

        recording.Clear();

        var transitionResponse = await sellerClient.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Received,
            Notes = "Pagamento confirmado"
        });
        transitionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.DispatchOutboxAsync();

        var message = recording.Messages
            .Where(x => x.Method == "OrderStatusUpdated")
            .Should().ContainSingle().Subject;
        message.Target.Should().Be($"user:{orderDetails.CustomerUserId}");
        ReadPayloadProperty(message.Args.Single(), "orderId").Should().Be(orderId);
        ReadPayloadProperty(message.Args.Single(), "orderCode").Should().Be(orderDetails.Code);
        ReadPayloadProperty(message.Args.Single(), "status").Should().Be(OrderStatus.Received);
        var changedAtUtc = (DateTime)ReadPayloadProperty(message.Args.Single(), "changedAtUtc")!;
        changedAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var durable = await customerClient.GetFromJsonAsync<CustomerNotificationsResponseDto>("/api/customer/notifications");
        durable!.Items.Should().ContainSingle(x => x.OrderId == orderId && x.Type == NotificationType.OrderReceived);
    }

    [Fact]
    public async Task UpdateOrderStatus_ShouldNotEmitOrderStatusUpdated_OnInvalidTransition()
    {
        var recording = GetCustomerRecordingHub();
        recording.Clear();

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

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Inválida", 20m);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "20",
            Street = "Rua Inválida",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });
        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        var createOrderResponse = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
        {
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id,
            PaymentMethod = PaymentMethod.PixOnline,
            Items = [new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 }]
        });
        createOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        var orderId = created!.OrderId;

        recording.Clear();

        var invalidTransitionResponse = await sellerClient.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Delivered
        });
        invalidTransitionResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        recording.Messages.Where(x => x.Method == "OrderStatusUpdated").Should().BeEmpty();
    }

    private RecordingHubContext<CustomerNotificationHub> GetCustomerRecordingHub()
    {
        return (RecordingHubContext<CustomerNotificationHub>)_factory.Services.GetRequiredService(typeof(IHubContext<CustomerNotificationHub>));
    }

    private static object? ReadPayloadProperty(object? payload, string name)
    {
        return payload?.GetType().GetProperty(name)?.GetValue(payload);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"signalr.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await _factory.RegisterSellerAsync(client, email, password, "SignalR Seller", "11982220000");

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja SignalR",
            Slug = $"loja-signalr-{Guid.NewGuid():N}",
            PhoneNumber = "11987770000",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (token.AccessToken, store!.Id);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"signalr.customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "SignalR Customer",
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
