using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Realtime.Hubs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// Seller cancellation endpoint: authoritative status, mandatory reason, store ownership, durable
/// customer notification and live SignalR delivery through the outbox.
/// </summary>
public sealed class OrderCancellationTests : IClassFixture<TestWebApplicationFactory>
{
    private const string CancelReason = "Cliente pediu para cancelar";
    private const string CustomerCancelMessage = "Seu pedido foi cancelado pela loja.";

    private readonly TestWebApplicationFactory _factory;

    public OrderCancellationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cancel_ShouldCancelReceivedOrder_AndNotifyTheCustomer()
    {
        var seller = await CreateSellerClientAsync();
        var customer = await CreateCustomerClientAsync();
        var orderId = await CreateDeliveryOrderAsync(seller.Client, customer, seller.StoreId);
        var recording = GetCustomerRecordingHub();
        recording.Clear();

        var response = await seller.Client.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel",
            new CancelOrderRequestDto { Reason = $"  {CancelReason}  " });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await response.Content.ReadFromJsonAsync<OrderDetailsResponseDto>();
        cancelled!.Status.Should().Be(OrderStatus.Cancelled);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId);
            order.Status.Should().Be(OrderStatus.Cancelled);
            order.CancellationReason.Should().Be(CancelReason);
            order.CancelledBy.Should().Be(OrderCancelledBy.Store);
            order.CancelledAtUtc.Should().NotBeNull();
            order.CancelledAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
            var history = await db.OrderStatusHistories.AsNoTracking()
                .SingleAsync(x => x.OrderId == orderId && x.NewStatus == OrderStatus.Cancelled);
            history.PreviousStatus.Should().Be(OrderStatus.Received);
            history.Notes.Should().Be(CancelReason);
        }

        var storeOrder = await seller.Client.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/store/{orderId}");
        storeOrder!.Status.Should().Be(OrderStatus.Cancelled);

        var notifications = await customer.GetFromJsonAsync<CustomerNotificationsResponseDto>("/api/customer/notifications");
        notifications!.Items.Should().ContainSingle(x => x.OrderId == orderId && x.Type == NotificationType.OrderCancelled)
            .Which.Message.Should().Be(CustomerCancelMessage);

        await _factory.DispatchOutboxAsync();

        var statusPush = recording.Messages
            .Where(x => x.Method == "OrderStatusUpdated")
            .Select(x => x.Args.Single())
            .SingleOrDefault(payload => (Guid)payload!.GetType().GetProperty("orderId")!.GetValue(payload)! == orderId);
        statusPush.Should().NotBeNull();
        ((int)statusPush!.GetType().GetProperty("status")!.GetValue(statusPush)!)
            .Should().Be((int)OrderStatus.Cancelled);

        var durablePush = recording.Messages
            .Where(x => x.Method == "ReceiveCustomerNotification")
            .Select(x => x.Args.Single())
            .SingleOrDefault(payload =>
                (Guid)payload!.GetType().GetProperty("orderId")!.GetValue(payload)! == orderId
                && (string?)payload.GetType().GetProperty("message")!.GetValue(payload) == CustomerCancelMessage);
        durablePush.Should().NotBeNull();
        durablePush!.GetType().GetProperty("message")!.GetValue(durablePush)!.Should().Be(CustomerCancelMessage);
    }

    [Fact]
    public async Task Cancel_ShouldReject_OrdersAfterPreparation_AndInvalidInput()
    {
        var seller = await CreateSellerClientAsync();
        var customer = await CreateCustomerClientAsync();
        var orderId = await CreateDeliveryOrderAsync(seller.Client, customer, seller.StoreId);

        (await seller.Client.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Preparing
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await seller.Client.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Ready
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var readyResponse = await seller.Client.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel",
            new CancelOrderRequestDto { Reason = CancelReason });
        readyResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await seller.Client.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/store/{orderId}"))!
            .Status.Should().Be(OrderStatus.Ready);
    }

    [Fact]
    public async Task Cancel_ShouldRequireFiveCharacters_AndStoreOwnership()
    {
        var seller = await CreateSellerClientAsync();
        var customer = await CreateCustomerClientAsync();
        var orderId = await CreateDeliveryOrderAsync(seller.Client, customer, seller.StoreId);

        var shortReason = await seller.Client.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel",
            new CancelOrderRequestDto { Reason = "abcd" });
        shortReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var otherSeller = await CreateSellerClientAsync();
        var forbidden = await otherSeller.Client.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel",
            new CancelOrderRequestDto { Reason = CancelReason });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await seller.Client.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/store/{orderId}"))!
            .Status.Should().Be(OrderStatus.Received);
    }

    [Fact]
    public async Task Cancel_ShouldBeIdempotent_ForARepeatedRequest()
    {
        var seller = await CreateSellerClientAsync();
        var customer = await CreateCustomerClientAsync();
        var orderId = await CreateDeliveryOrderAsync(seller.Client, customer, seller.StoreId);

        var first = await seller.Client.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel",
            new CancelOrderRequestDto { Reason = CancelReason });
        var second = await seller.Client.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel",
            new CancelOrderRequestDto { Reason = "Outro motivo qualquer" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.OrderStatusHistories.AsNoTracking()
            .CountAsync(x => x.OrderId == orderId && x.NewStatus == OrderStatus.Cancelled)).Should().Be(1);
        (await db.Notifications.AsNoTracking().CountAsync(x => x.OrderId == orderId && x.Type == NotificationType.OrderCancelled)).Should().Be(1);
        (await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId)).CancellationReason.Should().Be(CancelReason);
    }

    /// <summary>
    /// The generic status PATCH is not a cancellation path: Cancelled must be rejected there,
    /// otherwise the reason, actor, UTC timestamp, history, audit and notification would be
    /// bypassed. The dedicated POST /cancel remains the only way to cancel.
    /// </summary>
    [Fact]
    public async Task StatusPatch_ShouldRejectCancelled_AndKeepTheNormalTransitionsWorking()
    {
        var seller = await CreateSellerClientAsync();
        var customer = await CreateCustomerClientAsync();
        var orderId = await CreateDeliveryOrderAsync(seller.Client, customer, seller.StoreId);

        var response = await seller.Client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Cancelled, Notes = "bypass" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("status").GetInt32().Should().Be(400);
            body.RootElement.GetProperty("detail").GetString().Should().Be("Invalid order status transition.");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId);
            order.Status.Should().Be(OrderStatus.Received);
            order.CancellationReason.Should().BeNull();
            order.CancelledBy.Should().BeNull();
            order.CancelledAtUtc.Should().BeNull();

            (await db.OrderStatusHistories.AsNoTracking()
                .CountAsync(x => x.OrderId == orderId && x.NewStatus == OrderStatus.Cancelled)).Should().Be(0);
            (await db.AuditLogs.AsNoTracking()
                .CountAsync(x => x.EntityId == orderId && x.Event == "OrderCancelled")).Should().Be(0);
            (await db.Notifications.AsNoTracking()
                .CountAsync(x => x.OrderId == orderId && x.Type == NotificationType.OrderCancelled)).Should().Be(0);
            (await db.OutboxMessages.AsNoTracking()
                .CountAsync(x => x.AggregateId == orderId && x.Type == OutboxEventTypes.OrderStatusChanged)).Should().Be(0);
        }

        // The guard is scoped to Cancelled: the normal seller transition still works.
        var preparing = await seller.Client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Preparing });
        preparing.StatusCode.Should().Be(HttpStatusCode.OK);
        (await seller.Client.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/store/{orderId}"))!
            .Status.Should().Be(OrderStatus.Preparing);
    }

    private async Task<(HttpClient Client, Guid StoreId)> CreateSellerClientAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"cancel.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await _factory.RegisterSellerAsync(client, email, password, "Cancel Seller", "11982220000");
        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var store = await (await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Cancel",
            Slug = $"loja-cancel-{Guid.NewGuid():N}",
            PhoneNumber = "11987770000",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5,
        })).Content.ReadFromJsonAsync<StoreResponseDto>();

        await client.PatchAsJsonAsync($"/api/stores/{store!.Id}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await client.PatchAsJsonAsync($"/api/stores/{store.Id}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 4m,
            MinimumOrderValue = 10m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 4m } },
        });

        return (client, store.Id);
    }

    private async Task<HttpClient> CreateCustomerClientAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"cancel.customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cancel Customer",
            Email = email,
            Password = password,
            PhoneNumber = "11981118888"
        });
        await _factory.ConfirmEmailAsync(email);

        var login = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private static async Task<Guid> CreateDeliveryOrderAsync(HttpClient sellerClient, HttpClient customerClient, Guid storeId)
    {
        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Cancel", 20m);
        var address = await (await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "10",
            Street = "Rua Cancel",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        })).Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        var created = await (await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
        {
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            CashWithoutChange = true,
            Items = [new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 }]
        })).Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();

        return created!.OrderId;
    }

    private RecordingHubContext<CustomerNotificationHub> GetCustomerRecordingHub() =>
        (RecordingHubContext<CustomerNotificationHub>)_factory.Services.GetRequiredService(
            typeof(IHubContext<CustomerNotificationHub>));
}
