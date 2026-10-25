using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
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
        orderDetails!.Code.Should().NotBeNullOrWhiteSpace();
        orderDetails.Code.Should().NotStartWith("URB-");

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
    public async Task CreateOrder_ShouldEmitSellerAndCustomerNotifications_ThroughOutbox()
    {
        var sellerRecording = GetSellerRecordingHub();
        var customerRecording = GetCustomerRecordingHub();
        sellerRecording.Clear();
        customerRecording.Clear();

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

        Guid sellerUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            sellerUserId = (await db.Stores.FindAsync(storeId))!.OwnerUserId;
        }

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Tempo Real", 20m);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "20",
            Street = "Rua Tempo Real",
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
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Items = [new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 }]
        });
        createOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        var orderId = created!.OrderId;

        var orderDetails = await customerClient.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/{orderId}");

        await _factory.DispatchOutboxAsync();

        var sellerMessage = sellerRecording.Messages
            .Where(x => x.Method == "ReceiveSellerNotification")
            .Should().ContainSingle().Subject;
        sellerMessage.Target.Should().Be($"user:{sellerUserId}");
        sellerMessage.Args.Should().ContainSingle();
        ReadPayloadProperty(sellerMessage.Args.Single(), "orderId").Should().Be(orderId);
        ReadPayloadProperty(sellerMessage.Args.Single(), "type").Should().Be(NotificationType.NewOrder);
        ReadPayloadProperty(sellerMessage.Args.Single(), "title").Should().Be("Novo pedido recebido");
        ReadPayloadProperty(sellerMessage.Args.Single(), "id").Should().NotBeNull();
        ReadPayloadProperty(sellerMessage.Args.Single(), "createdAtUtc").Should().BeOfType<DateTime>();

        var customerMessage = customerRecording.Messages
            .Where(x => x.Method == "ReceiveCustomerNotification")
            .Should().ContainSingle().Subject;
        customerMessage.Target.Should().Be($"user:{orderDetails!.CustomerUserId}");
        ReadPayloadProperty(customerMessage.Args.Single(), "orderId").Should().Be(orderId);
        ReadPayloadProperty(customerMessage.Args.Single(), "type").Should().Be(NotificationType.OrderReceived);
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

    [Fact]
    public async Task UpdateOrderStatus_ShouldEmitSellerPrintingEvent_OnlyOnAcceptance()
    {
        var sellerRecording = GetSellerRecordingHub();
        sellerRecording.Clear();

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

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.StorePrinterConfigs.Add(new StorePrinterConfig
            {
                StoreId = storeId,
                PrinterPresetId = Guid.NewGuid(),
                PrinterName = "POS-58",
                AutoPrint = true
            });
            await db.SaveChangesAsync();
        }

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Aceite", 20m);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "30",
            Street = "Rua Aceite",
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

        var receivedResponse = await sellerClient.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Received
        });
        receivedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.DispatchOutboxAsync();
        sellerRecording.Messages.Where(x => x.Method == "OrderAcceptedForPrinting").Should().BeEmpty();

        sellerRecording.Clear();

        var acceptedResponse = await sellerClient.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Preparing
        });
        acceptedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.DispatchOutboxAsync();

        var message = sellerRecording.Messages
            .Where(x => x.Method == "OrderAcceptedForPrinting")
            .Should().ContainSingle().Subject;
        message.Target.Should().StartWith("user:");
        ReadPayloadProperty(message.Args.Single(), "orderId").Should().Be(orderId);
        ReadPayloadProperty(message.Args.Single(), "storeId").Should().Be(storeId);
        ((int)ReadPayloadProperty(message.Args.Single(), "sequence")!).Should().BeGreaterThan(0);
        var acceptedAtUtc = (DateTime)ReadPayloadProperty(message.Args.Single(), "acceptedAtUtc")!;
        acceptedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task SellerPrintingJobs_should_recover_acceptance_and_only_ack_for_the_owning_store()
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

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.StorePrinterConfigs.Add(new StorePrinterConfig
            {
                StoreId = storeId,
                PrinterPresetId = Guid.NewGuid(),
                PrinterName = "POS-58",
                AutoPrint = true
            });
            await db.SaveChangesAsync();
        }

        var customerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Recuperacao", 20m);
        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "40",
            Street = "Rua Recuperacao",
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

        (await sellerClient.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Received
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.DispatchOutboxAsync();

        (await sellerClient.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Preparing
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.DispatchOutboxAsync();

        var pending = await sellerClient.GetFromJsonAsync<PendingPrintJobsResponseDto>("/api/seller/printing/jobs?afterCursor=0&limit=50");
        pending.Should().NotBeNull();
        pending!.Items.Should().ContainSingle();
        var job = pending.Items.Single();
        job.OrderId.Should().Be(orderId);
        job.StoreId.Should().Be(storeId);
        job.Cursor.Should().BeGreaterThan(0);
        job.AcceptedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        job.LastError.Should().BeNull();

        var otherSellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (otherToken, _) = await RegisterLoginAndCreateStoreAsync(otherSellerClient, "Hamburguer");
        otherSellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var otherPending = await otherSellerClient.GetFromJsonAsync<PendingPrintJobsResponseDto>("/api/seller/printing/jobs?afterCursor=0&limit=50");
        otherPending!.Items.Should().BeEmpty();

        var forbiddenAck = await otherSellerClient.PostAsJsonAsync($"/api/seller/printing/jobs/{orderId}/result", new RecordPrintJobResultRequestDto
        {
            Success = true,
            OperationKey = "other-key"
        });
        forbiddenAck.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ack = await sellerClient.PostAsJsonAsync($"/api/seller/printing/jobs/{orderId}/result", new RecordPrintJobResultRequestDto
        {
            Success = true,
            OperationKey = "order-key"
        });
        ack.StatusCode.Should().Be(HttpStatusCode.OK);
        var ackResult = await ack.Content.ReadFromJsonAsync<RecordPrintJobResultResponseDto>();
        ackResult!.Job.Should().NotBeNull();

        var afterAck = await sellerClient.GetFromJsonAsync<PendingPrintJobsResponseDto>("/api/seller/printing/jobs?afterCursor=0&limit=50");
        afterAck!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AutoPrint_disabled_should_not_emit_print_event_or_create_print_job()
    {
        var sellerRecording = GetSellerRecordingHub();
        sellerRecording.Clear();

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

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Sem AutoPrint", 20m);
        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "50",
            Street = "Rua Sem AutoPrint",
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

        (await sellerClient.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Received
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.DispatchOutboxAsync();
        sellerRecording.Clear();

        (await sellerClient.PatchAsJsonAsync($"/api/orders/{orderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Preparing
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.DispatchOutboxAsync();

        sellerRecording.Messages.Where(x => x.Method == "OrderAcceptedForPrinting").Should().BeEmpty();
        var pending = await sellerClient.GetFromJsonAsync<PendingPrintJobsResponseDto>("/api/seller/printing/jobs?afterCursor=0&limit=50");
        pending!.Items.Should().BeEmpty();
    }

    private RecordingHubContext<CustomerNotificationHub> GetCustomerRecordingHub()
    {
        return (RecordingHubContext<CustomerNotificationHub>)_factory.Services.GetRequiredService(typeof(IHubContext<CustomerNotificationHub>));
    }

    private RecordingHubContext<SellerNotificationHub> GetSellerRecordingHub()
    {
        return (RecordingHubContext<SellerNotificationHub>)_factory.Services.GetRequiredService(typeof(IHubContext<SellerNotificationHub>));
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
