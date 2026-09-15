using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.IntegrationTests.Infrastructure;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Payments;

namespace Urbeat.IntegrationTests.Api;

public sealed class MockPixPaymentFlowTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private static WebApplicationFactory<Program> CreateFactory(FakeMockPixClock clock, FakeMockPixRandom random)
    {
        return new TestWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Payments:Provider"] = "Mock",
                    ["Payments:WindowSeconds"] = "60",
                    ["Payments:ApprovalMinimumSeconds"] = "10",
                    ["Payments:ApprovalMaximumSeconds"] = "50",
                    ["Payments:WorkerPollingIntervalSeconds"] = "2",
                    ["Payments:WorkerEnabled"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMockPixClock>();
                services.AddSingleton<IMockPixClock>(clock);
                services.RemoveAll<IMockPixRandom>();
                services.AddSingleton<IMockPixRandom>(random);
            });
        });
    }

    [Fact]
    public async Task Mock_ShouldCreatePendingPayment_WithServerProvidedExpiresAtUtc()
    {
        var clock = new FakeMockPixClock(FixedNow);
        using var factory = CreateFactory(clock, new FakeMockPixRandom(0, 20));

        var (customerClient, _, order, payment) = await CreatePendingMockPaymentAsync(factory);

        payment.OrderId.Should().Be(order.OrderId);
        payment.Gateway.Should().Be(PaymentGateway.Mock);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.GatewayTransactionId.Should().NotBeNullOrWhiteSpace();
        payment.GatewayCheckoutUrl.Should().Be(MockPixPaymentStrategy.MockCheckoutUrl);
        payment.ExpiresAtUtc.Should().Be(FixedNow.AddSeconds(60));

        var tracked = await customerClient.GetFromJsonAsync<OrderPaymentResponseDto>($"/api/payments/order/{order.OrderId}");
        tracked.Should().NotBeNull();
        tracked!.ExpiresAtUtc.Should().Be(FixedNow.AddSeconds(60));
        tracked.History.Should().ContainSingle(x => x.NewStatus == PaymentStatus.Pending);
    }

    [Fact]
    public async Task Mock_ShouldApprovePayment_AdvanceOrder_AndEmitOutboxEvent()
    {
        var clock = new FakeMockPixClock(FixedNow);
        using var factory = CreateFactory(clock, new FakeMockPixRandom(0, 20));

        var (customerClient, _, order, payment) = await CreatePendingMockPaymentAsync(factory);

        clock.Advance(TimeSpan.FromSeconds(21));

        using (var scope = factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<MockPixPaymentProcessor>();
            (await processor.ProcessDuePaymentsAsync()).Should().Be(1);
        }

        var tracked = await customerClient.GetFromJsonAsync<OrderPaymentResponseDto>($"/api/payments/order/{order.OrderId}");
        tracked!.Status.Should().Be(PaymentStatus.Paid);
        tracked.History.Count(x => x.NewStatus == PaymentStatus.Paid && x.Source == "Mock").Should().Be(1);

        var orderDetails = await customerClient.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/{order.OrderId}");
        orderDetails!.Status.Should().Be(OrderStatus.Received);
        orderDetails.History.Count(x => x.NewStatus == OrderStatus.Received).Should().Be(1);

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasOutboxEvent = await db.OutboxMessages.AnyAsync(x =>
            x.Type == OutboxEventTypes.OrderStatusChanged
            && x.AggregateId == order.OrderId
            && x.Payload.Contains("Mock", StringComparison.Ordinal));
        hasOutboxEvent.Should().BeTrue();
    }

    [Fact]
    public async Task Mock_ShouldExpirePayment_AndAllowRetry_WithoutCreatingSecondOrder()
    {
        var clock = new FakeMockPixClock(FixedNow);
        var random = new FakeMockPixRandom(1); // first attempt expires
        using var factory = CreateFactory(clock, random);

        var (customerClient, _, order, payment) = await CreatePendingMockPaymentAsync(factory);

        clock.Advance(TimeSpan.FromSeconds(61));

        using (var scope = factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<MockPixPaymentProcessor>();
            (await processor.ProcessDuePaymentsAsync()).Should().Be(1);
        }

        var failedPayment = await customerClient.GetFromJsonAsync<OrderPaymentResponseDto>($"/api/payments/order/{order.OrderId}");
        failedPayment!.Status.Should().Be(PaymentStatus.Failed);
        failedPayment.History.Count(x => x.NewStatus == PaymentStatus.Failed && x.Source == "Mock").Should().Be(1);

        var orderAfterExpiry = await customerClient.GetFromJsonAsync<OrderDetailsResponseDto>($"/api/orders/{order.OrderId}");
        orderAfterExpiry!.Status.Should().Be(OrderStatus.PendingPayment);

        random.Enqueue(0, 25); // retry is approved after 25s
        var retryStart = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = order.OrderId
        });
        retryStart.StatusCode.Should().Be(HttpStatusCode.OK);
        var retried = await retryStart.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        retried!.PaymentId.Should().Be(payment.PaymentId);
        retried.Status.Should().Be(PaymentStatus.Pending);
        retried.ExpiresAtUtc.Should().Be(FixedNow.AddSeconds(121));

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Payments.CountAsync(x => x.OrderId == order.OrderId)).Should().Be(1);
        (await db.Orders.CountAsync(x => x.Id == order.OrderId)).Should().Be(1);
        var reloadedPayment = await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.OrderId);
        reloadedPayment.Attempt.Should().Be(2);

        (await db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == reloadedPayment.Id
            && x.PreviousStatus == PaymentStatus.Failed
            && x.NewStatus == PaymentStatus.Pending
            && x.Source == "Checkout")).Should().BeTrue();
    }

    [Fact]
    public async Task Mock_ShouldAllowRetry_WhenPendingWindowElapsed_BeforeWorkerFinalizes()
    {
        var clock = new FakeMockPixClock(FixedNow);
        var random = new FakeMockPixRandom(1); // first attempt expires
        using var factory = CreateFactory(clock, random);

        var (customerClient, _, order, payment) = await CreatePendingMockPaymentAsync(factory);

        // The UI countdown reached zero, but the worker has not yet finalized the expired attempt,
        // so the backend still reports the payment as Pending with an already-elapsed deadline.
        clock.Advance(TimeSpan.FromSeconds(61));

        var stillPending = await customerClient.GetFromJsonAsync<OrderPaymentResponseDto>($"/api/payments/order/{order.OrderId}");
        stillPending!.Status.Should().Be(PaymentStatus.Pending);
        stillPending.ExpiresAtUtc.Should().Be(FixedNow.AddSeconds(60));

        random.Enqueue(0, 25); // retry is approved after 25s
        var retryStart = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = order.OrderId
        });
        retryStart.StatusCode.Should().Be(HttpStatusCode.OK);
        var retried = await retryStart.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();
        retried!.PaymentId.Should().Be(payment.PaymentId);
        retried.Status.Should().Be(PaymentStatus.Pending);
        retried.ExpiresAtUtc.Should().Be(FixedNow.AddSeconds(121));

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Payments.CountAsync(x => x.OrderId == order.OrderId)).Should().Be(1);
        (await db.Orders.CountAsync(x => x.Id == order.OrderId)).Should().Be(1);
        var reloadedPayment = await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.OrderId);
        reloadedPayment.Attempt.Should().Be(2);
        reloadedPayment.Status.Should().Be(PaymentStatus.Pending);
        reloadedPayment.MockExpiresAtUtc.Should().Be(FixedNow.AddSeconds(121));

        (await db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == reloadedPayment.Id
            && x.PreviousStatus == PaymentStatus.Pending
            && x.NewStatus == PaymentStatus.Failed
            && x.Source == "Mock")).Should().BeTrue();

        (await db.PaymentStatusHistories.AnyAsync(x => x.PaymentId == reloadedPayment.Id
            && x.PreviousStatus == PaymentStatus.Failed
            && x.NewStatus == PaymentStatus.Pending
            && x.Source == "Checkout")).Should().BeTrue();
    }

    private async Task<(HttpClient Customer, HttpClient Seller, CheckoutConfirmResponseDto Order, OrderPaymentResponseDto Payment)> CreatePendingMockPaymentAsync(
        WebApplicationFactory<Program> factory)
    {
        var sellerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, storeId) = await RegisterLoginAndCreateStoreAsync(factory, sellerClient);
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await sellerClient.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 5m,
            MinimumOrderValue = 10m,
            DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m } },
        });

        var customerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        var customerToken = await RegisterAndLoginCustomerAsync(factory, customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var addressResponse = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "777",
            Street = "Rua Mock Pix",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        });
        var address = await addressResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();

        var productId = await ProductTestHelper.CreateProductAsync(sellerClient, storeId, "Pizza Mock Pix", 25m);

        var createOrderResponse = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
        {
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = address!.Id,
            PaymentMethod = PaymentMethod.PixOnline,
            Items =
            [
                new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 }
            ]
        });
        createOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createOrderResponse.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();

        var paymentStartResponse = await customerClient.PostAsJsonAsync("/api/payments/order", new CreateOrderPaymentRequestDto
        {
            OrderId = order!.OrderId
        });
        paymentStartResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payment = await paymentStartResponse.Content.ReadFromJsonAsync<OrderPaymentResponseDto>();

        return (customerClient, sellerClient, order, payment!);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client)
    {
        var email = $"mockpix.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Mock Pix Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11980000002"
        });
        await factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Mock Pix",
            Slug = "loja-mock-pix",
            PhoneNumber = "11987770001",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (token.AccessToken, store!.Id);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var email = $"mockpix.customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Mock Pix Customer",
            Email = email,
            Password = password,
            PhoneNumber = "11981110001"
        });
        await factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        return token!.AccessToken;
    }

    private sealed class FakeMockPixClock : IMockPixClock
    {
        private DateTime _utcNow;

        public FakeMockPixClock(DateTime utcNow) => _utcNow = utcNow;

        public DateTime UtcNow => _utcNow;

        public void Advance(TimeSpan span) => _utcNow = _utcNow.Add(span);
    }

    private sealed class FakeMockPixRandom : IMockPixRandom
    {
        private readonly Queue<int> _values;

        public FakeMockPixRandom(params int[] values)
        {
            _values = new Queue<int>(values);
        }

        public void Enqueue(params int[] values)
        {
            foreach (var value in values)
            {
                _values.Enqueue(value);
            }
        }

        public int Next(int minimumInclusive, int maximumExclusive) =>
            _values.Count > 0 ? _values.Dequeue() : minimumInclusive;
    }
}
