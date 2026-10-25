using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Outbox;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class DeliveryDriverFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "SenhaForte123";
    private readonly TestWebApplicationFactory _factory;

    public DeliveryDriverFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InviteAccept_ShouldWorkForSameDriverInTwoStores_AndIsolateData()
    {
        var (sellerAClient, storeAId) = await CreateStoreAsync("A");
        var (sellerBClient, storeBId) = await CreateStoreAsync("B");
        var email = $"driver.{Guid.NewGuid():N}@urbeat.local";

        var inviteA = await InviteDriverAsync(sellerAClient, email, "Entregador A", "11999990001");
        var inviteB = await InviteDriverAsync(sellerBClient, email, "Entregador B", "11999990002");

        var driverClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var tokenA = await ReadInviteTokenAsync(inviteA.Id);
        var tokenB = await ReadInviteTokenAsync(inviteB.Id);

        var preview = await driverClient.GetFromJsonAsync<DeliveryDriverInvitePreviewResponseDto>(
            $"/api/delivery-drivers/invites/{tokenA}");
        preview.Should().NotBeNull();
        preview!.Valid.Should().BeTrue();
        preview.AlreadyAccepted.Should().BeFalse();

        var acceptA = await driverClient.PostAsJsonAsync(
            $"/api/delivery-drivers/invites/{tokenA}/accept",
            new AcceptDeliveryDriverInviteRequestDto { Password = Password, FullName = "Entregador Global" });
        acceptA.StatusCode.Should().Be(HttpStatusCode.OK, await acceptA.Content.ReadAsStringAsync());

        var acceptB = await driverClient.PostAsJsonAsync(
            $"/api/delivery-drivers/invites/{tokenB}/accept",
            new AcceptDeliveryDriverInviteRequestDto { Password = Password });
        acceptB.StatusCode.Should().Be(HttpStatusCode.OK, await acceptB.Content.ReadAsStringAsync());

        var replay = await driverClient.PostAsJsonAsync(
            $"/api/delivery-drivers/invites/{tokenA}/accept",
            new AcceptDeliveryDriverInviteRequestDto { Password = Password });
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest, "invites are single-use");

        var driverToken = await LoginDriverAsync(driverClient, email);
        driverClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);

        var stores = await driverClient.GetFromJsonAsync<List<DeliveryDriverStoreResponseDto>>("/api/driver/stores");
        stores.Should().NotBeNull();
        stores!.Should().HaveCount(2);
        stores.Select(x => x.StoreId).Should().Contain([storeAId, storeBId]);

        var storeAList = await sellerAClient.GetFromJsonAsync<List<SellerDeliveryDriverResponseDto>>(
            "/api/delivery-drivers/store");
        storeAList.Should().NotBeNull();
        storeAList!.Should().ContainSingle(x => x.Email == email && x.Status == StoreDeliveryDriverStatus.Active);

        var revokeResponse = await sellerAClient.PostAsync(
            $"/api/delivery-drivers/store/{inviteA.Id}/revoke", null);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var ordersForRevokedStore = await driverClient.GetAsync($"/api/driver/orders?storeId={storeAId}");
        ordersForRevokedStore.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Assignment_ShouldRejectCrossStore_AndDriverShouldOnlySeeOwnStoreOrders()
    {
        var (sellerAClient, storeAId) = await CreateStoreAsync("A");
        var (sellerBClient, storeBId) = await CreateStoreAsync("B");
        var email = $"driver.assignment.{Guid.NewGuid():N}@urbeat.local";

        var invite = await InviteDriverAsync(sellerAClient, email, "Entregador A", "11999990001");
        var driverClient = await AcceptInviteAndLoginAsync(invite.Id, email);

        var orderA = await CreateDeliveryOrderAsync(sellerAClient, storeAId);
        var orderB = await CreateDeliveryOrderAsync(sellerBClient, storeBId);

        var crossStore = await sellerBClient.PutAsJsonAsync(
            $"/api/orders/store/{orderB.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        crossStore.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var assignA = await sellerAClient.PutAsJsonAsync(
            $"/api/orders/store/{orderA.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        assignA.StatusCode.Should().Be(HttpStatusCode.OK, await assignA.Content.ReadAsStringAsync());
        var assigned = await assignA.Content.ReadFromJsonAsync<OrderDetailsResponseDto>();
        assigned!.DeliveryDriverName.Should().Be("Entregador A");

        var driverOrders = await driverClient.GetFromJsonAsync<DriverOrdersResultDto>(
            $"/api/driver/orders?storeId={storeAId}");
        driverOrders.Should().NotBeNull();
        driverOrders!.Orders.Should().ContainSingle(x => x.Id == orderA.OrderId);
        driverOrders.Orders.Should().NotContain(x => x.Id == orderB.OrderId);

        var otherStoreOrders = await driverClient.GetAsync($"/api/driver/orders?storeId={storeBId}");
        otherStoreOrders.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var secondDriverEmail = $"driver.assignment.second.{Guid.NewGuid():N}@urbeat.local";
        var secondInvite = await InviteDriverAsync(sellerAClient, secondDriverEmail, "Entregador Dois", "11999990003");
        var secondDriverClient = await AcceptInviteAndLoginAsync(secondInvite.Id, secondDriverEmail);

        var secondDriverOrders = await secondDriverClient.GetFromJsonAsync<DriverOrdersResultDto>(
            $"/api/driver/orders?storeId={storeAId}");
        secondDriverOrders!.Orders.Should().BeEmpty("orders are isolated per driver");
        var secondDriverOrderDetail = await secondDriverClient.GetAsync($"/api/driver/orders/{orderA.OrderId}");
        secondDriverOrderDetail.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DriverOrders_ShouldExposePersistedStoreAndCustomerCoordinates_AndNullWhenMissing()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var email = $"driver.coords.{Guid.NewGuid():N}@urbeat.local";
        var invite = await InviteDriverAsync(sellerClient, email, "Entregador Rotas", "11999990001");
        var driverClient = await AcceptInviteAndLoginAsync(invite.Id, email);

        const double storeLatitude = -23.55052;
        const double storeLongitude = -46.633308;
        const double customerLatitude = -23.561414;
        const double customerLongitude = -46.655881;

        await SetStoreAddressAsync(sellerClient, storeId, storeLatitude, storeLongitude);

        var withCoordinates = await CreateDeliveryOrderAsync(
            sellerClient, storeId, customerLatitude, customerLongitude);
        var withoutCoordinates = await CreateDeliveryOrderAsync(sellerClient, storeId);

        foreach (var orderId in new[] { withCoordinates.OrderId, withoutCoordinates.OrderId })
        {
            var assign = await sellerClient.PutAsJsonAsync(
                $"/api/orders/store/{orderId}/delivery-driver",
                new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
            assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());
        }

        var list = await driverClient.GetFromJsonAsync<DriverOrdersResultDto>(
            $"/api/driver/orders?storeId={storeId}");
        list.Should().NotBeNull();

        var mapped = list!.Orders.Single(x => x.Id == withCoordinates.OrderId);
        mapped.OriginLatitude.Should().Be(storeLatitude);
        mapped.OriginLongitude.Should().Be(storeLongitude);
        mapped.DestinationLatitude.Should().Be(customerLatitude);
        mapped.DestinationLongitude.Should().Be(customerLongitude);

        var missing = list.Orders.Single(x => x.Id == withoutCoordinates.OrderId);
        missing.OriginLatitude.Should().Be(storeLatitude);
        missing.OriginLongitude.Should().Be(storeLongitude);
        missing.DestinationLatitude.Should().BeNull("the customer address has no coordinates");
        missing.DestinationLongitude.Should().BeNull("the customer address has no coordinates");

        var detail = await driverClient.GetFromJsonAsync<DriverOrderSummaryResponseDto>(
            $"/api/driver/orders/{withCoordinates.OrderId}");
        detail.Should().NotBeNull();
        detail!.DestinationLatitude.Should().Be(customerLatitude);
        detail.DestinationLongitude.Should().Be(customerLongitude);
    }

    [Fact]
    public async Task DeliveryOrder_ShouldKeepCheckoutCoordinateSnapshot_WhenCustomerAddressIsEdited()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var email = $"driver.snapshot.{Guid.NewGuid():N}@urbeat.local";
        var invite = await InviteDriverAsync(sellerClient, email, "Entregador Snapshot", "11999990001");
        var driverClient = await AcceptInviteAndLoginAsync(invite.Id, email);

        const double checkoutLatitude = -23.55052;
        const double checkoutLongitude = -46.633308;
        const double editedLatitude = -22.906847;
        const double editedLongitude = -43.172897;

        var order = await CreateDeliveryOrderAsync(
            sellerClient, storeId, checkoutLatitude, checkoutLongitude);

        var addresses = await order.CustomerClient.GetFromJsonAsync<List<CustomerAddressResponseDto>>(
            "/api/customer/addresses");
        var address = addresses!.Single();
        var updateAddress = await order.CustomerClient.PutAsJsonAsync(
            $"/api/customer/addresses/{address.Id}",
            new UpsertCustomerAddressRequestDto
            {
                Cep = address.Cep,
                Number = "999",
                Street = "Rua Editada Depois do Checkout",
                Neighborhood = address.Neighborhood,
                City = address.City,
                State = address.State,
                IsPrimary = address.IsPrimary,
                Latitude = editedLatitude,
                Longitude = editedLongitude
            });
        updateAddress.StatusCode.Should().Be(HttpStatusCode.OK, await updateAddress.Content.ReadAsStringAsync());

        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        var list = await driverClient.GetFromJsonAsync<DriverOrdersResultDto>(
            $"/api/driver/orders?storeId={storeId}");
        var mapped = list!.Orders.Single(x => x.Id == order.OrderId);

        mapped.DestinationLatitude.Should().Be(checkoutLatitude,
            "the order must keep the coordinates captured at checkout");
        mapped.DestinationLongitude.Should().Be(checkoutLongitude);
        mapped.AddressNumber.Should().Be("100", "the address text is also an order snapshot");
        mapped.AddressStreet.Should().Be("Rua Entrega");
    }

    [Fact]
    public async Task Revoke_ShouldBeBlocked_WhileAssignedOrderStillNeedsDriverToConfirmPin()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var email = $"driver.revoke.pending.{Guid.NewGuid():N}@urbeat.local";
        var invite = await InviteDriverAsync(sellerClient, email, "Entregador Revogacao", "11999990001");
        var driverClient = await AcceptInviteAndLoginAsync(invite.Id, email);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        var customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");

        var start = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var revokeOnDelivery = await sellerClient.PostAsync(
            $"/api/delivery-drivers/store/{invite.Id}/revoke", null);
        revokeOnDelivery.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await revokeOnDelivery.Content.ReadAsStringAsync()).Should().Contain("PIN");

        var markDelivered = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Delivered });
        markDelivered.StatusCode.Should().Be(HttpStatusCode.OK, await markDelivered.Content.ReadAsStringAsync());

        var revokeAwaitingPin = await sellerClient.PostAsync(
            $"/api/delivery-drivers/store/{invite.Id}/revoke", null);
        revokeAwaitingPin.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "delivered orders without PIN confirmation still depend on the assigned driver");

        var confirm = await driverClient.PostAsJsonAsync(
            $"/api/driver/orders/{order.OrderId}/confirm-pin",
            new ConfirmDeliveryPinRequestDto { Pin = customerDetails!.DeliveryPin! });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        var revokeAfterPin = await sellerClient.PostAsync(
            $"/api/delivery-drivers/store/{invite.Id}/revoke", null);
        revokeAfterPin.StatusCode.Should().Be(HttpStatusCode.OK, await revokeAfterPin.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PinAttempts_ShouldStopAtLimit_AndPersistExactlyFiveAttempts()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var email = $"driver.limit.{Guid.NewGuid():N}@urbeat.local";
        var invite = await InviteDriverAsync(sellerClient, email, "Entregador Limite", "11999990001");
        var driverClient = await AcceptInviteAndLoginAsync(invite.Id, email);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        var customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        var start = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var wrongPin = customerDetails!.DeliveryPin == "000000" ? "000001" : "000000";
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await driverClient.PostAsJsonAsync(
                $"/api/driver/orders/{order.OrderId}/confirm-pin",
                new ConfirmDeliveryPinRequestDto { Pin = wrongPin });

            response.StatusCode.Should().Be(
                attempt < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.Locked,
                $"attempt {attempt} must respect the configured limit");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignment = await dbContext.OrderDeliveryAssignments
                .AsNoTracking()
                .SingleAsync(x => x.OrderId == order.OrderId);
            assignment.PinAttemptCount.Should().Be(5, "the persisted counter must never exceed the limit");
        }

        var correctPin = await driverClient.PostAsJsonAsync(
            $"/api/driver/orders/{order.OrderId}/confirm-pin",
            new ConfirmDeliveryPinRequestDto { Pin = customerDetails.DeliveryPin! });
        correctPin.StatusCode.Should().Be(HttpStatusCode.Locked);
    }

    [Fact]
    public async Task StartAndPinConfirmation_ShouldTransitionOrder_AndExposePinOnlyToCustomer()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var email = $"driver.pin.{Guid.NewGuid():N}@urbeat.local";
        var invite = await InviteDriverAsync(sellerClient, email, "Entregador Pin", "11999990001");
        var driverClient = await AcceptInviteAndLoginAsync(invite.Id, email);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);

        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        var customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        customerDetails.Should().NotBeNull();
        customerDetails!.DeliveryPin.Should().MatchRegex(@"^\d{6}$");

        var sellerDetails = await sellerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/store/{order.OrderId}");
        sellerDetails!.DeliveryPin.Should().BeNull("the PIN must never be exposed to the seller");

        var start = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var action = await start.Content.ReadFromJsonAsync<DeliveryDriverActionResultDto>();
        action!.Status.Should().Be(OrderStatus.OnDelivery);

        var wrongPin = await driverClient.PostAsJsonAsync(
            $"/api/driver/orders/{order.OrderId}/confirm-pin",
            new ConfirmDeliveryPinRequestDto { Pin = "000000" });
        // A generated PIN can legitimately be 000000; retry with another wrong value if so.
        if (wrongPin.StatusCode == HttpStatusCode.OK)
        {
            customerDetails.DeliveryPin.Should().Be("000000");
            wrongPin = await driverClient.PostAsJsonAsync(
                $"/api/driver/orders/{order.OrderId}/confirm-pin",
                new ConfirmDeliveryPinRequestDto { Pin = "111111" });
        }

        wrongPin.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var locked = false;
        foreach (var attempt in new[] { "999998", "999997", "999996", "999995", "999994" })
        {
            var response = await driverClient.PostAsJsonAsync(
                $"/api/driver/orders/{order.OrderId}/confirm-pin",
                new ConfirmDeliveryPinRequestDto { Pin = attempt });
            if (response.StatusCode == HttpStatusCode.Locked)
            {
                locked = true;
                break;
            }
        }

        locked.Should().BeTrue("the backend must lock the PIN after the configured attempts");

        var correctPin = await driverClient.PostAsJsonAsync(
            $"/api/driver/orders/{order.OrderId}/confirm-pin",
            new ConfirmDeliveryPinRequestDto { Pin = customerDetails.DeliveryPin! });
        correctPin.StatusCode.Should().Be(HttpStatusCode.Locked, "the attempt limit must be enforced");

        // Reassigning resets the attempt counter by design and lets the same driver try again.
        var reassign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        reassign.StatusCode.Should().Be(HttpStatusCode.Conflict, "the delivery already started");

        var remove = await sellerClient.DeleteAsync($"/api/orders/store/{order.OrderId}/delivery-driver");
        remove.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var deliveredBySeller = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Delivered });
        deliveredBySeller.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeWithoutPin = await sellerClient.PostAsync($"/api/orders/{order.OrderId}/complete", null);
        completeWithoutPin.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CorrectPin_ShouldConfirmDelivery_EnableSellerCompletion_AndAuditUtc()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var email = $"driver.success.{Guid.NewGuid():N}@urbeat.local";
        var invite = await InviteDriverAsync(sellerClient, email, "Entregador Success", "11999990001");
        var driverClient = await AcceptInviteAndLoginAsync(invite.Id, email);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK);

        var customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");

        var start = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirm = await driverClient.PostAsJsonAsync(
            $"/api/driver/orders/{order.OrderId}/confirm-pin",
            new ConfirmDeliveryPinRequestDto { Pin = customerDetails!.DeliveryPin! });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        var result = await confirm.Content.ReadFromJsonAsync<DeliveryDriverActionResultDto>();
        result!.Status.Should().Be(OrderStatus.Delivered);
        result.DeliveryConfirmedAtUtc.Should().NotBeNull();
        result.DeliveryConfirmedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);

        var complete = await sellerClient.PostAsync($"/api/orders/{order.OrderId}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pinAudit = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(x => x.Event == "DeliveryPinConfirmed" && x.EntityId == order.OrderId);
        pinAudit.UserId.Should().NotBeNull();
        pinAudit.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var stored = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
        stored.DeliveryConfirmedAtUtc.Should().NotBeNull();
        stored.DeliveryPinConfirmedAtUtc.Should().NotBeNull();
        stored.DeliveryPinConfirmedByDriverId.Should().NotBeNull();
        stored.DeliveryPinEncrypted.Should().NotBeNull();
        stored.DeliveryPinHash.Should().NotBeNull();
        stored.DeliveryPinHash.Should().NotBe(customerDetails.DeliveryPin);
    }

    [Fact]
    public async Task PickupOrder_ShouldNotAcceptDeliveryDriverAssignment()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var email = $"driver.pickup.{Guid.NewGuid():N}@urbeat.local";
        var invite = await InviteDriverAsync(sellerClient, email, "Entregador Pickup", "11999990001");

        var pickupOrderId = await CreateOrderAsync(sellerClient, storeId, FulfillmentType.PickUp, null);

        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{pickupOrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<(HttpClient Client, Guid StoreId)> CreateStoreAsync(string suffix)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"delivery.seller.{suffix}.{Guid.NewGuid():N}@urbeat.local";

        await _factory.RegisterSellerAsync(client, email, Password, $"Delivery Seller {suffix}", "11982220000");
        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = Password
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStore = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = $"Loja Delivery {suffix}",
            Slug = $"loja-delivery-{suffix.ToLowerInvariant()}-{Guid.NewGuid():N}",
            PhoneNumber = "11987770000",
            CuisineType = "Lanches",
            SupportsDelivery = true,
            SupportsPickup = true,
            MaxDeliveryRadiusKm = 10
        });
        createStore.StatusCode.Should().Be(HttpStatusCode.Created, await createStore.Content.ReadAsStringAsync());
        var store = await createStore.Content.ReadFromJsonAsync<StoreResponseDto>();

        await client.PatchAsJsonAsync($"/api/stores/{store!.Id}/status", new UpdateStoreStatusRequestDto { IsOpen = true });
        await client.PatchAsJsonAsync($"/api/stores/{store.Id}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 4m,
            MinimumOrderValue = 5m,
            DeliveryAreas = [new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 4m }]
        });

        return (client, store.Id);
    }

    private async Task<SellerDeliveryDriverResponseDto> InviteDriverAsync(
        HttpClient sellerClient,
        string email,
        string name,
        string phone)
    {
        var response = await sellerClient.PostAsJsonAsync("/api/delivery-drivers/store", new InviteDeliveryDriverRequestDto
        {
            FullName = name,
            Email = email,
            PhoneNumber = phone
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var driver = await response.Content.ReadFromJsonAsync<SellerDeliveryDriverResponseDto>();
        driver.Should().NotBeNull();
        driver!.Status.Should().Be(StoreDeliveryDriverStatus.Pending);
        return driver;
    }

    private async Task<string> ReadInviteTokenAsync(Guid storeDriverId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(x => x.Type == OutboxEventTypes.OutboundMessageRequested && x.AggregateId == storeDriverId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstAsync();
        var payload = OutboxPayloadSerializer.Deserialize<OutboundMessageRequestedEvent>(message.Payload);
        var match = Regex.Match(payload.Body, @"/entregador/convite/([^""<\s]+)");
        match.Success.Should().BeTrue("invite e-mail must contain the acceptance link");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    private async Task<HttpClient> AcceptInviteAndLoginAsync(Guid storeDriverId, string email)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var token = await ReadInviteTokenAsync(storeDriverId);
        var accept = await client.PostAsJsonAsync(
            $"/api/delivery-drivers/invites/{token}/accept",
            new AcceptDeliveryDriverInviteRequestDto { Password = Password });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());

        var driverToken = await LoginDriverAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
        return client;
    }

    private async Task<string> LoginDriverAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login/driver", new LoginRequestDto
        {
            Email = email,
            Password = Password
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        return token!.AccessToken;
    }

    private async Task<(Guid OrderId, HttpClient CustomerClient)> CreateDeliveryOrderAsync(
        HttpClient sellerClient,
        Guid storeId,
        double? addressLatitude = null,
        double? addressLongitude = null)
    {
        var customerClient = await CreateCustomerClientAsync();
        var productId = await ProductTestHelper.CreateProductAsync(
            sellerClient, storeId, $"Produto {Guid.NewGuid():N}", 20m);
        var addressId = await CreateAddressAsync(customerClient, addressLatitude, addressLongitude);

        var orderId = await CreateOrderAsync(
            sellerClient,
            storeId,
            FulfillmentType.Delivery,
            addressId,
            customerClient,
            productId);

        await AdvanceToReadyAsync(sellerClient, orderId);
        return (orderId, customerClient);
    }

    private async Task<Guid> CreateOrderAsync(
        HttpClient sellerClient,
        Guid storeId,
        FulfillmentType fulfillmentType,
        Guid? addressId,
        HttpClient? customerClient = null,
        Guid? productId = null)
    {
        if (customerClient is null)
        {
            customerClient = await CreateCustomerClientAsync();
        }

        productId ??= await ProductTestHelper.CreateProductAsync(
            sellerClient, storeId, $"Produto {Guid.NewGuid():N}", 20m);
        addressId ??= await CreateAddressAsync(customerClient);

        var response = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
        {
            StoreId = storeId,
            FulfillmentType = fulfillmentType,
            CustomerAddressId = fulfillmentType == FulfillmentType.Delivery ? addressId : null,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Items =
            [
                new CheckoutItemRequestDto { ProductId = productId.Value, Quantity = 1 }
            ]
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();
        return created!.OrderId;
    }

    private static async Task AdvanceToReadyAsync(HttpClient sellerClient, Guid orderId)
    {
        foreach (var status in new[] { OrderStatus.Preparing, OrderStatus.Ready })
        {
            var response = await sellerClient.PatchAsJsonAsync(
                $"/api/orders/{orderId}/status",
                new UpdateOrderStatusRequestDto { NewStatus = status });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
    }

    private async Task<HttpClient> CreateCustomerClientAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"delivery.customer.{Guid.NewGuid():N}@urbeat.local";

        var register = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Delivery",
            Email = email,
            Password = Password,
            PhoneNumber = "11981118888"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created, await register.Content.ReadAsStringAsync());
        await _factory.ConfirmEmailAsync(email);

        var login = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = Password
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private static async Task<Guid> CreateAddressAsync(
        HttpClient customerClient,
        double? latitude = null,
        double? longitude = null)
    {
        var response = await customerClient.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "100",
            Street = "Rua Entrega",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true,
            Latitude = latitude,
            Longitude = longitude
        });
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var address = await response.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        return address!.Id;
    }

    private static async Task SetStoreAddressAsync(
        HttpClient sellerClient,
        Guid storeId,
        double? latitude = null,
        double? longitude = null)
    {
        var response = await sellerClient.PutAsJsonAsync($"/api/stores/{storeId}/address",
            new UpdateStoreAddressRequestDto
            {
                Street = "Rua da Loja",
                Number = "10",
                Neighborhood = "Centro",
                City = "Sao Paulo",
                State = "SP",
                ZipCode = "01001000",
                Latitude = latitude,
                Longitude = longitude
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
