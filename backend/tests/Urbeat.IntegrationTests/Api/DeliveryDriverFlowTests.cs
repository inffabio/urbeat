using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
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
        customerDetails!.DeliveryPin.Should().BeNull("the PIN must not be exposed before OnDelivery");

        var start = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        customerDetails!.DeliveryPin.Should().NotBeNullOrWhiteSpace();

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

        var start = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");

        var wrongPin = customerDetails!.DeliveryPin == "0000" ? "0001" : "0000";
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
        customerDetails!.DeliveryPin.Should().BeNull(
            "the customer only sees the PIN after the order enters OnDelivery");

        var start = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var action = await start.Content.ReadFromJsonAsync<DeliveryDriverActionResultDto>();
        action!.Status.Should().Be(OrderStatus.OnDelivery);

        customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        customerDetails!.DeliveryPin.Should().MatchRegex(@"^\d{4}$");

        var sellerDetails = await sellerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/store/{order.OrderId}");
        sellerDetails!.DeliveryPin.Should().BeNull("the PIN must never be exposed to the seller");

        var wrongPin = await driverClient.PostAsJsonAsync(
            $"/api/driver/orders/{order.OrderId}/confirm-pin",
            new ConfirmDeliveryPinRequestDto { Pin = "0000" });
        // A generated PIN can legitimately be 0000; retry with another wrong value if so.
        if (wrongPin.StatusCode == HttpStatusCode.OK)
        {
            customerDetails.DeliveryPin.Should().Be("0000");
            wrongPin = await driverClient.PostAsJsonAsync(
                $"/api/driver/orders/{order.OrderId}/confirm-pin",
                new ConfirmDeliveryPinRequestDto { Pin = "1111" });
        }

        wrongPin.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var locked = false;
        foreach (var attempt in new[] { "9998", "9997", "9996", "9995", "9994" })
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

        var start = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");

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

    [Fact]
    public async Task StoreDriverCrud_ShouldWorkWithoutEmailOrInvite_AndExposeWhatsApp()
    {
        var (sellerClient, _) = await CreateStoreAsync("CRUD");

        var created = await CreateSimpleDriverAsync(
            sellerClient,
            "Motoboy Simples",
            "(11) 99999-0011",
            "(11) 99999-0012",
            isActive: true);

        created.Email.Should().BeEmpty();
        created.HasAccount.Should().BeFalse();
        created.IsActive.Should().BeTrue();
        created.PhoneNumber.Should().Be("11999990011", "the mask is never persisted");
        created.WhatsAppPhoneNumber.Should().Be("11999990012");

        var list = await sellerClient.GetFromJsonAsync<List<SellerDeliveryDriverResponseDto>>(
            "/api/delivery-drivers/store");
        list.Should().NotBeNull();
        list!.Should().ContainSingle(x => x.Id == created.Id && x.WhatsAppPhoneNumber == "11999990012");

        var update = await sellerClient.PutAsJsonAsync(
            $"/api/delivery-drivers/{created.Id}",
            new UpsertStoreDeliveryDriverRequestDto
            {
                Name = "Motoboy Atualizado",
                PhoneNumber = "(11) 99999-0013",
                WhatsAppPhoneNumber = "(11) 99999-0014",
                IsActive = true
            });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var updated = await update.Content.ReadFromJsonAsync<SellerDeliveryDriverResponseDto>();
        updated!.Name.Should().Be("Motoboy Atualizado");
        updated.PhoneNumber.Should().Be("11999990013");
        updated.WhatsAppPhoneNumber.Should().Be("11999990014");

        var deactivate = await sellerClient.PatchAsJsonAsync(
            $"/api/delivery-drivers/{created.Id}/status",
            new SetStoreDeliveryDriverStatusRequestDto { IsActive = false });
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK, await deactivate.Content.ReadAsStringAsync());
        var inactive = await deactivate.Content.ReadFromJsonAsync<SellerDeliveryDriverResponseDto>();
        inactive!.IsActive.Should().BeFalse();
        inactive.Status.Should().Be(StoreDeliveryDriverStatus.Revoked);

        var reactivate = await sellerClient.PatchAsJsonAsync(
            $"/api/delivery-drivers/{created.Id}/status",
            new SetStoreDeliveryDriverStatusRequestDto { IsActive = true });
        reactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reactivate.Content.ReadFromJsonAsync<SellerDeliveryDriverResponseDto>())!
            .IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("1199999888")]
    [InlineData("(00) 99999-8888")]
    [InlineData("(11) 89999-8888")]
    [InlineData("(11) 99999-8888x")]
    public async Task CreateStoreDriver_ShouldRejectNonMobilePhone(string phone)
    {
        var (sellerClient, _) = await CreateStoreAsync("INVALID-PHONE");

        var response = await sellerClient.PostAsJsonAsync(
            "/api/delivery-drivers",
            new UpsertStoreDeliveryDriverRequestDto
            {
                Name = "Motoboy Inválido",
                PhoneNumber = phone,
                WhatsAppPhoneNumber = "(11) 99999-8888",
                IsActive = true
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteStoreDriver_ShouldRemoveLinkAndOrphanDriver_WhenNoOrderAssigned()
    {
        var (sellerClient, _) = await CreateStoreAsync("DELETE-OK");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Excluir", "11999990051", "11999990052", isActive: true);

        var delete = await sellerClient.DeleteAsync($"/api/delivery-drivers/{driver.Id}");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent, await delete.Content.ReadAsStringAsync());

        var list = await sellerClient.GetFromJsonAsync<List<SellerDeliveryDriverResponseDto>>(
            "/api/delivery-drivers/store");
        list.Should().NotBeNull();
        list!.Should().NotContain(x => x.Id == driver.Id);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.StoreDeliveryDrivers.AsNoTracking().AnyAsync(x => x.Id == driver.Id))
            .Should().BeFalse();
        (await dbContext.DeliveryDrivers.AsNoTracking().AnyAsync(x => x.Id == driver.DeliveryDriverId))
            .Should().BeFalse("a driver used only by this store is removed together with the link");
        (await dbContext.AuditLogs.AsNoTracking().AnyAsync(x =>
                x.Event == "DeliveryDriverDeleted" && x.EntityId == driver.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task DeleteStoreDriver_ShouldReturnConflict_ForActiveAndHistoricalAssignments()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("DELETE-BLOCK");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Com Pedido", "11999990053", "11999990054", isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        var blockedActive = await sellerClient.DeleteAsync($"/api/delivery-drivers/{driver.Id}");
        blockedActive.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blockedActive.Content.ReadAsStringAsync()).Should().Contain("Inative");

        var start = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = driver.Id
            });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var delivered = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Delivered });
        delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());

        var blockedHistorical = await sellerClient.DeleteAsync($"/api/delivery-drivers/{driver.Id}");
        blockedHistorical.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "historical order assignments must keep the driver available for inactivating only");

        var list = await sellerClient.GetFromJsonAsync<List<SellerDeliveryDriverResponseDto>>(
            "/api/delivery-drivers/store");
        list!.Should().ContainSingle(x => x.Id == driver.Id);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.StoreDeliveryDrivers.AsNoTracking().AnyAsync(x => x.Id == driver.Id))
            .Should().BeTrue();
        (await dbContext.DeliveryDrivers.AsNoTracking().AnyAsync(x => x.Id == driver.DeliveryDriverId))
            .Should().BeTrue();
        (await dbContext.OrderDeliveryAssignments.AsNoTracking().AnyAsync(x => x.OrderId == order.OrderId))
            .Should().BeTrue("deleting a driver must never cascade to order assignments");
    }

    [Fact]
    public async Task DeleteStoreDriver_ShouldNotAcceptDriverFromAnotherStoreOrUnknownId()
    {
        var (sellerAClient, _) = await CreateStoreAsync("DELETE-CROSS-A");
        var (sellerBClient, _) = await CreateStoreAsync("DELETE-CROSS-B");
        var driver = await CreateSimpleDriverAsync(
            sellerAClient, "Motoboy Loja A", "11999990055", null, isActive: true);

        var crossStore = await sellerBClient.DeleteAsync($"/api/delivery-drivers/{driver.Id}");
        crossStore.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var unknown = await sellerAClient.DeleteAsync($"/api/delivery-drivers/{Guid.NewGuid()}");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await sellerAClient.GetFromJsonAsync<List<SellerDeliveryDriverResponseDto>>(
            "/api/delivery-drivers/store");
        list!.Should().ContainSingle(x => x.Id == driver.Id);
    }

    [Fact]
    public async Task DeleteStoreDriver_ShouldKeepSharedLegacyIdentity_UsedByAnotherStore()
    {
        var (sellerAClient, _) = await CreateStoreAsync("DELETE-SHARED-A");
        var (sellerBClient, _) = await CreateStoreAsync("DELETE-SHARED-B");
        var email = $"driver.shared.delete.{Guid.NewGuid():N}@urbeat.local";

        var inviteA = await InviteDriverAsync(sellerAClient, email, "Entregador Compartilhado", "11999990001");
        var inviteB = await InviteDriverAsync(sellerBClient, email, "Entregador Compartilhado", "11999990001");
        await AcceptInviteAndLoginAsync(inviteA.Id, email);
        await AcceptInviteAndLoginAsync(inviteB.Id, email);

        inviteB.DeliveryDriverId.Should().Be(inviteA.DeliveryDriverId,
            "the legacy invite flow shares one global driver between stores");

        var delete = await sellerAClient.DeleteAsync($"/api/delivery-drivers/{inviteA.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent, await delete.Content.ReadAsStringAsync());

        var storeAList = await sellerAClient.GetFromJsonAsync<List<SellerDeliveryDriverResponseDto>>(
            "/api/delivery-drivers/store");
        storeAList!.Should().NotContain(x => x.Id == inviteA.Id);

        var storeBList = await sellerBClient.GetFromJsonAsync<List<SellerDeliveryDriverResponseDto>>(
            "/api/delivery-drivers/store");
        storeBList!.Should().ContainSingle(x => x.Id == inviteB.Id && x.Email == email);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.DeliveryDrivers.AsNoTracking().AnyAsync(x => x.Id == inviteA.DeliveryDriverId))
            .Should().BeTrue("the identity is still used by the other store");
    }

    [Fact]
    public async Task OnDeliveryTransition_ShouldRequireActiveDriverFromTheSameStore()
    {
        var (sellerAClient, storeAId) = await CreateStoreAsync("TRANSITION-A");
        var (sellerBClient, storeBId) = await CreateStoreAsync("TRANSITION-B");

        var order = await CreateDeliveryOrderAsync(sellerAClient, storeAId);
        var inactiveDriver = await CreateSimpleDriverAsync(
            sellerAClient, "Motoboy Inativo", "11999990021", null, isActive: false);
        var otherStoreDriver = await CreateSimpleDriverAsync(
            sellerBClient, "Motoboy Outra Loja", "11999990022", null, isActive: true);
        var activeDriver = await CreateSimpleDriverAsync(
            sellerAClient, "Motoboy Ativo", "11999990023", "11999990024", isActive: true);

        var noDriver = await sellerAClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.OnDelivery });
        noDriver.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var inactive = await sellerAClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = inactiveDriver.Id
            });
        inactive.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var crossStore = await sellerAClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = otherStoreDriver.Id
            });
        crossStore.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var started = await sellerAClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = activeDriver.Id
            });
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
        var details = await started.Content.ReadFromJsonAsync<OrderDetailsResponseDto>();
        details!.Status.Should().Be(OrderStatus.OnDelivery);
        details.DeliveryDriverName.Should().Be("Motoboy Ativo");
        details.DeliveryStartedAtUtc.Should().NotBeNull();
        details.DeliveryDriverId.Should().Be(activeDriver.DeliveryDriverId);

        // A second store cannot create a route for this order/driver combination.
        var foreignRoute = await sellerBClient.PostAsJsonAsync(
            "/api/delivery-routes/store",
            new CreateDeliveryRouteRequestDto
            {
                DeliveryDriverId = otherStoreDriver.Id,
                OrderIds = [order.OrderId]
            });
        foreignRoute.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeliveryRoute_ShouldSharePublicLink_AdvanceWithPin_AndBlockAfterRevoke()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("ROUTE");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Rota", "11999990031", "11999990032", isActive: true);

        var first = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var second = await CreateDeliveryOrderAsync(sellerClient, storeId);

        foreach (var order in new[] { first, second })
        {
            var start = await sellerClient.PatchAsJsonAsync(
                $"/api/orders/{order.OrderId}/status",
                new UpdateOrderStatusRequestDto
                {
                    NewStatus = OrderStatus.OnDelivery,
                    DeliveryDriverId = driver.Id
                });
            start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        }

        var create = await sellerClient.PostAsJsonAsync(
            "/api/delivery-routes/store",
            new CreateDeliveryRouteRequestDto
            {
                DeliveryDriverId = driver.Id,
                OrderIds = [second.OrderId, first.OrderId]
            });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var created = await create.Content.ReadFromJsonAsync<DeliveryRouteOperationResultDto>();
        created!.Route.Should().NotBeNull();
        created.Route!.TotalStops.Should().Be(2);
        created.Route.Orders.Select(x => x.OrderId).Should().Equal([second.OrderId, first.OrderId]);
        created.ShareUrl.Should().NotBeNullOrWhiteSpace();

        var share = await sellerClient.PostAsync($"/api/delivery-routes/store/{created.Route.Id}/share", null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var shared = await share.Content.ReadFromJsonAsync<DeliveryRouteOperationResultDto>();
        shared!.ShareUrl.Should().Contain("/rota/");
        var token = ExtractRouteToken(shared.ShareUrl);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var publicRoute = await publicClient.GetFromJsonAsync<PublicDeliveryRouteResponseDto>(
            $"/api/public/delivery-routes/{token}");
        publicRoute.Should().NotBeNull();
        var publicRouteJson = await publicClient.GetStringAsync($"/api/public/delivery-routes/{token}");
        publicRouteJson.Should().NotContain("deliveryPin", "the public route never exposes the PIN");
        publicRoute!.StoreName.Should().Be("Loja Delivery ROUTE");
        publicRoute.DriverName.Should().Be("Motoboy Rota");
        publicRoute.Stops.Should().HaveCount(2);
        publicRoute.NextSequence.Should().Be(1);
        publicRoute.Stops.Single(x => x.Sequence == 1).OrderCode.Should().Be(
            created.Route.Orders.Single(x => x.Sequence == 1).Code);

        var secondPin = (await second.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{second.OrderId}"))!.DeliveryPin!;
        secondPin.Should().Be("8888", "the PIN comes from the customer's phone, not a random value");
        var wrongPin = secondPin == "0000" ? "0001" : "0000";
        var wrong = await publicClient.PostAsJsonAsync(
            $"/api/public/delivery-routes/{token}/stops/1/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = wrongPin });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var pending = await publicClient.GetFromJsonAsync<PublicDeliveryRouteResponseDto>(
            $"/api/public/delivery-routes/{token}");
        pending!.DeliveredStops.Should().Be(0);
        pending.NextSequence.Should().Be(1);

        var confirmFirst = await publicClient.PostAsJsonAsync(
            $"/api/public/delivery-routes/{token}/stops/1/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = secondPin });
        confirmFirst.StatusCode.Should().Be(HttpStatusCode.OK, await confirmFirst.Content.ReadAsStringAsync());

        var afterFirst = await publicClient.GetFromJsonAsync<PublicDeliveryRouteResponseDto>(
            $"/api/public/delivery-routes/{token}");
        afterFirst!.DeliveredStops.Should().Be(1);
        afterFirst.NextSequence.Should().Be(2);
        afterFirst.Stops.Single(x => x.Sequence == 2).CanDeliver.Should().BeTrue();

        var repeated = await publicClient.PostAsJsonAsync(
            $"/api/public/delivery-routes/{token}/stops/1/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = secondPin });
        repeated.StatusCode.Should().Be(HttpStatusCode.OK, "repeating a confirmed stop is idempotent");

        var updateAfterDelivery = await sellerClient.PutAsJsonAsync(
            $"/api/delivery-routes/store/{created.Route.Id}",
            new UpdateDeliveryRouteRequestDto
            {
                DeliveryDriverId = driver.Id,
                OrderIds = [second.OrderId, first.OrderId]
            });
        updateAfterDelivery.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a delivered stop must stay editable as route history");

        var firstPin = (await first.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{first.OrderId}"))!.DeliveryPin!;
        var confirmSecond = await publicClient.PostAsJsonAsync(
            $"/api/public/delivery-routes/{token}/stops/2/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = firstPin });
        confirmSecond.StatusCode.Should().Be(HttpStatusCode.OK, await confirmSecond.Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storedFirst = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == second.OrderId);
            storedFirst.Status.Should().Be(OrderStatus.Delivered);
            storedFirst.DeliveryConfirmedAtUtc.Should().NotBeNull();
            storedFirst.DeliveryConfirmedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);

            var route = await dbContext.DeliveryRoutes.AsNoTracking().SingleAsync(x => x.Id == created.Route.Id);
            route.CompletedAtUtc.Should().NotBeNull();
        }

        var revoke = await sellerClient.PostAsync(
            $"/api/delivery-routes/store/{created.Route.Id}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        var revoked = await publicClient.GetAsync($"/api/public/delivery-routes/{token}");
        revoked.StatusCode.Should().Be(HttpStatusCode.Gone);

        var revokedAction = await publicClient.PostAsJsonAsync(
            $"/api/public/delivery-routes/{token}/stops/1/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = secondPin });
        revokedAction.StatusCode.Should().Be(HttpStatusCode.Gone);

        var invalid = await publicClient.GetAsync(
            $"/api/public/delivery-routes/{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}");
        invalid.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SendDeliveryRoutes_ShouldGroupByDriver_AndReturnOneAbsoluteLinkPerMotoboy()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("SEND");
        var driverA = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Envio A", "11999990201", "11999990202", isActive: true);
        var driverB = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Envio B", "11999990203", null, isActive: true);

        var firstA = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var secondA = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var firstB = await CreateDeliveryOrderAsync(sellerClient, storeId);

        await StartDeliveryAsync(sellerClient, firstA.OrderId, driverA.Id);
        await StartDeliveryAsync(sellerClient, secondA.OrderId, driverA.Id);
        await StartDeliveryAsync(sellerClient, firstB.OrderId, driverB.Id);

        var send = await sellerClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [firstA.OrderId, firstB.OrderId, secondA.OrderId],
                PhysicalConfirmation = true
            });
        send.StatusCode.Should().Be(HttpStatusCode.OK, await send.Content.ReadAsStringAsync());

        var result = await send.Content.ReadFromJsonAsync<SendDeliveryRoutesResultDto>();
        result.Should().NotBeNull();
        result!.Groups.Should().HaveCount(2);

        var groupA = result.Groups.Single(x => x.DeliveryDriverId == driverA.DeliveryDriverId);
        var groupB = result.Groups.Single(x => x.DeliveryDriverId == driverB.DeliveryDriverId);

        groupA.DriverName.Should().Be("Motoboy Envio A");
        groupA.OrderCount.Should().Be(2);
        groupA.TotalStops.Should().Be(2);
        groupA.Route.Orders.Select(x => x.OrderId).Should().BeEquivalentTo(
            [firstA.OrderId, secondA.OrderId]);
        groupA.Route.Orders.Select(x => x.OrderId).Should().NotContain(firstB.OrderId);
        groupA.Route.Orders.Should().OnlyContain(x => x.CustomerName == "Cliente Delivery");
        groupA.Route.Orders.Should().OnlyContain(
            x => x.AddressSummary == "Rua Entrega, 100 - Centro");
        new Uri(groupA.ShareUrl).IsAbsoluteUri.Should().BeTrue();
        groupA.ShareUrl.Should().Contain("/rota/");
        groupA.Message.Should().Contain(groupA.ShareUrl);
        groupA.WhatsAppUrl.Should().NotBeNull();
        groupA.WhatsAppUrl.Should().Contain("https://wa.me/5511999990202");

        groupB.OrderCount.Should().Be(1);
        groupB.Route.Orders.Select(x => x.OrderId).Should().Equal([firstB.OrderId]);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var publicA = await publicClient.GetFromJsonAsync<PublicDeliveryRouteResponseDto>(
            $"/api/public/delivery-routes/{ExtractRouteToken(groupA.ShareUrl)}");
        publicA.Should().NotBeNull();
        publicA!.DriverName.Should().Be("Motoboy Envio A");
        publicA.Stops.Should().HaveCount(2);
        publicA.Stops.Select(x => x.OrderCode).Should().NotContain(
            groupB.Route.Orders.Single().Code);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.DeliveryRoutes
            .AsNoTracking()
            .CountAsync(x => x.StoreId == storeId && x.StoreDeliveryDriverId == driverA.Id))
            .Should().Be(1);
        (await dbContext.DeliveryRoutes
            .AsNoTracking()
            .CountAsync(x => x.StoreId == storeId && x.StoreDeliveryDriverId == driverB.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task SendDeliveryRoutes_ShouldReuseRouteAndRotateToken_WithoutDuplicatingStops()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("RESEND");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Reenvio", "11999990301", "11999990302", isActive: true);

        var first = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var second = await CreateDeliveryOrderAsync(sellerClient, storeId);
        await StartDeliveryAsync(sellerClient, first.OrderId, driver.Id);
        await StartDeliveryAsync(sellerClient, second.OrderId, driver.Id);

        var firstSend = await sellerClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [first.OrderId],
                PhysicalConfirmation = true
            });
        firstSend.StatusCode.Should().Be(HttpStatusCode.OK, await firstSend.Content.ReadAsStringAsync());
        var firstResult = await firstSend.Content.ReadFromJsonAsync<SendDeliveryRoutesResultDto>();
        var firstGroup = firstResult!.Groups.Single();
        var oldToken = ExtractRouteToken(firstGroup.ShareUrl);

        var secondSend = await sellerClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [first.OrderId, second.OrderId],
                PhysicalConfirmation = true
            });
        secondSend.StatusCode.Should().Be(HttpStatusCode.OK, await secondSend.Content.ReadAsStringAsync());
        var secondResult = await secondSend.Content.ReadFromJsonAsync<SendDeliveryRoutesResultDto>();
        var secondGroup = secondResult!.Groups.Single();

        secondGroup.Route.Id.Should().Be(firstGroup.Route.Id, "an active route is reused per motoboy");
        secondGroup.OrderCount.Should().Be(1);
        secondGroup.TotalStops.Should().Be(2);
        secondGroup.ShareUrl.Should().NotBe(firstGroup.ShareUrl, "resharing rotates the public token");

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var oldLink = await publicClient.GetAsync($"/api/public/delivery-routes/{oldToken}");
        oldLink.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var newRoute = await publicClient.GetFromJsonAsync<PublicDeliveryRouteResponseDto>(
            $"/api/public/delivery-routes/{ExtractRouteToken(secondGroup.ShareUrl)}");
        newRoute!.Stops.Should().HaveCount(2);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.DeliveryRoutes
            .AsNoTracking()
            .CountAsync(x => x.StoreId == storeId && x.StoreDeliveryDriverId == driver.Id))
            .Should().Be(1);
        (await dbContext.DeliveryRouteOrders
            .AsNoTracking()
            .CountAsync(x => x.DeliveryRouteId == firstGroup.Route.Id))
            .Should().Be(2, "the same order must not be added twice");
    }

    [Fact]
    public async Task SendDeliveryRoutes_ShouldRejectCrossStore_InvalidStatusAndOrderWithoutDriver()
    {
        var (sellerAClient, storeAId) = await CreateStoreAsync("SEND-A");
        var (sellerBClient, storeBId) = await CreateStoreAsync("SEND-B");
        var driver = await CreateSimpleDriverAsync(
            sellerAClient, "Motoboy Envio Validação", "11999990401", "11999990402", isActive: true);

        var orderA = await CreateDeliveryOrderAsync(sellerAClient, storeAId);
        var orderB = await CreateDeliveryOrderAsync(sellerBClient, storeBId);
        await StartDeliveryAsync(sellerAClient, orderA.OrderId, driver.Id);

        var crossStoreByA = await sellerAClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [orderB.OrderId],
                PhysicalConfirmation = true
            });
        crossStoreByA.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var crossStoreByB = await sellerBClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [orderA.OrderId],
                PhysicalConfirmation = true
            });
        crossStoreByB.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var readyOrder = await CreateDeliveryOrderAsync(sellerAClient, storeAId);
        var invalidStatus = await sellerAClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [readyOrder.OrderId],
                PhysicalConfirmation = true
            });
        invalidStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadApiErrorAsync(invalidStatus)).Should().Contain("não está em entrega");

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var withoutDriver = await dbContext.Orders.SingleAsync(x => x.Id == orderB.OrderId);
            withoutDriver.Status = OrderStatus.OnDelivery;
            withoutDriver.StatusVersion += 1;
            await dbContext.SaveChangesAsync();
        }

        var noDriver = await sellerBClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [orderB.OrderId],
                PhysicalConfirmation = true
            });
        noDriver.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadApiErrorAsync(noDriver)).Should().Contain("sem motoboy");
    }

    [Fact]
    public async Task SendDeliveryRoutes_ShouldRejectOrderAlreadyInAnotherDriverActiveRoute()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("SEND-CONFLICT");
        var driverA = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Conflito A", "11999990501", "11999990502", isActive: true);
        var driverB = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Conflito B", "11999990503", "11999990504", isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        await StartDeliveryAsync(sellerClient, order.OrderId, driverA.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var route = new DeliveryRoute
            {
                StoreId = storeId,
                DeliveryDriverId = driverB.DeliveryDriverId,
                StoreDeliveryDriverId = driverB.Id,
                CreatedByUserId = Guid.NewGuid(),
                TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
            };
            await dbContext.DeliveryRoutes.AddAsync(route);
            await dbContext.DeliveryRouteOrders.AddAsync(new DeliveryRouteOrder
            {
                DeliveryRouteId = route.Id,
                OrderId = order.OrderId,
                Sequence = 1
            });
            await dbContext.SaveChangesAsync();
        }

        var send = await sellerClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [order.OrderId],
                PhysicalConfirmation = true
            });

        send.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadApiErrorAsync(send)).Should().Contain("outro motoboy");
    }

    [Fact]
    public async Task SendDeliveryRoutes_ShouldRequireExplicitPhysicalConfirmation()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("SEND-CONFIRM");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Conferência", "11999990601", "11999990602", isActive: true);
        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        await StartDeliveryAsync(sellerClient, order.OrderId, driver.Id);

        var explicitFalse = await sellerClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [order.OrderId],
                PhysicalConfirmation = false
            });
        explicitFalse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await explicitFalse.Content.ReadAsStringAsync()).Should().Contain("fisicamente");

        var absent = await sellerClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new { orderIds = new[] { order.OrderId } });
        absent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await absent.Content.ReadAsStringAsync()).Should().Contain("fisicamente");

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await dbContext.DeliveryRoutes
                .AsNoTracking()
                .CountAsync(x => x.StoreId == storeId && x.StoreDeliveryDriverId == driver.Id))
                .Should().Be(0, "an unconfirmed request must not create a route");
        }

        var confirmed = await sellerClient.PostAsJsonAsync(
            "/api/delivery-routes/store/send",
            new SendDeliveryRoutesRequestDto
            {
                OrderIds = [order.OrderId],
                PhysicalConfirmation = true
            });
        confirmed.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await confirmed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendDeliveryRoutes_ShouldNotCreateDuplicateActiveRoutesForConcurrentRequests()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("SEND-CONCURRENT");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Concorrência", "11999990701", "11999990702", isActive: true);

        var first = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var second = await CreateDeliveryOrderAsync(sellerClient, storeId);
        await StartDeliveryAsync(sellerClient, first.OrderId, driver.Id);
        await StartDeliveryAsync(sellerClient, second.OrderId, driver.Id);

        var payload = new SendDeliveryRoutesRequestDto
        {
            OrderIds = [first.OrderId, second.OrderId],
            PhysicalConfirmation = true
        };

        var responses = await Task.WhenAll(
            sellerClient.PostAsJsonAsync("/api/delivery-routes/store/send", payload),
            sellerClient.PostAsJsonAsync("/api/delivery-routes/store/send", payload));

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
        }

        var results = await Task.WhenAll(
            responses.Select(response =>
                response.Content.ReadFromJsonAsync<SendDeliveryRoutesResultDto>()));
        var routeIds = results
            .Select(result => result!.Groups.Single().Route.Id)
            .Distinct()
            .ToArray();
        routeIds.Should().HaveCount(1, "both requests must reuse the same active route");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.DeliveryRoutes
            .AsNoTracking()
            .CountAsync(x => x.StoreId == storeId
                && x.StoreDeliveryDriverId == driver.Id
                && x.RevokedAtUtc == null
                && x.CompletedAtUtc == null))
            .Should().Be(1);
        (await dbContext.DeliveryRouteOrders
            .AsNoTracking()
            .CountAsync(x => x.DeliveryRouteId == routeIds[0]))
            .Should().Be(2, "concurrent sends must not duplicate stops");
    }

    [Fact]
    public async Task Pin_ShouldBeGeneratedOnlyOnOnDelivery_WithExactlyFourDigits()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("PIN-GEN");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy PIN", "11999990041", "11999990042", isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);

        var beforeAssignment = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        beforeAssignment!.DeliveryPin.Should().BeNull("the PIN must not exist before OnDelivery");

        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        var beforeDelivery = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        beforeDelivery!.DeliveryPin.Should().BeNull("the PIN is issued only by the OnDelivery transition");

        var start = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = driver.Id
            });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var afterDelivery = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        afterDelivery!.Status.Should().Be(OrderStatus.OnDelivery);
        afterDelivery.DeliveryPin.Should().MatchRegex(@"^\d{4}$");

        var sellerDetails = await sellerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/store/{order.OrderId}");
        sellerDetails!.DeliveryPin.Should().BeNull("the PIN is never exposed to the seller");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
        var customerPhone = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == stored.CustomerUserId)
            .Select(x => x.PhoneNumber)
            .SingleAsync();
        var expectedPin = customerPhone![^4..];
        afterDelivery.DeliveryPin.Should().Be(
            expectedPin,
            "the PIN is always the last four digits of the customer's phone");
        stored.DeliveryPinHash.Should().NotBeNullOrWhiteSpace();
        stored.DeliveryPinHash.Should().NotBe(afterDelivery.DeliveryPin);
        stored.DeliveryPinEncrypted.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Pin_ShouldPreserveLeadingZeros_WhenPhoneHasExactlyFourDigits()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("PIN-ZERO");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Zero", "11999990049", null, isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        await SetCustomerPhoneAsync(order.OrderId, "0000");

        var start = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = driver.Id
            });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var details = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        details!.DeliveryPin.Should().Be("0000", "the PIN stays a 4-digit string, leading zeros included");
    }

    [Fact]
    public async Task OnDeliveryTransition_ShouldRejectCustomerWithoutValidPhone_AndUseTheLastFourDigitsAfterFix()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("PIN-PHONE");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Telefone", "11999990047", null, isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        await SetCustomerPhoneAsync(order.OrderId, "123");

        var rejected = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = driver.Id
            });
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("telefone");

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.Status.Should().Be(OrderStatus.Ready);
            stored.DeliveryPinHash.Should().BeNull("no PIN can be issued without a customer phone");
        }

        await SetCustomerPhoneAsync(order.OrderId, "(11) 99876-5432");

        var accepted = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = driver.Id
            });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        var details = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        details!.DeliveryPin.Should().Be("5432");
    }

    [Fact]
    public async Task DriverStart_ShouldRejectCustomerWithoutValidPhone()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("PIN-DRIVER-PHONE");
        var email = $"driver.phone.{Guid.NewGuid():N}@urbeat.local";
        var invite = await InviteDriverAsync(sellerClient, email, "Entregador Telefone", "11999990048");
        var driverClient = await AcceptInviteAndLoginAsync(invite.Id, email);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = invite.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        await SetCustomerPhoneAsync(order.OrderId, null);

        var rejected = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("telefone");

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.Status.Should().Be(OrderStatus.Ready);
            stored.DeliveryPinHash.Should().BeNull();
        }

        await SetCustomerPhoneAsync(order.OrderId, "11912345678");

        var started = await driverClient.PostAsync($"/api/driver/orders/{order.OrderId}/start", null);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());

        var details = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        details!.DeliveryPin.Should().Be("5678");
    }

    [Fact]
    public async Task PinConfirmation_ShouldRejectNonFourDigitPayload_WithoutChangingTheOrder()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("PIN-LENGTH");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy PIN Tamanho", "11999990043", null, isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var start = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = driver.Id
            });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var pin = (await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}"))!.DeliveryPin!;
        pin.Should().Be("8888", "the PIN is the last four digits of the customer's phone");

        var share = await sellerClient.PostAsync(
            $"/api/orders/store/{order.OrderId}/delivery-link",
            null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var token = ExtractRouteToken(
            (await share.Content.ReadFromJsonAsync<ShareIndividualDeliveryResultDto>())!.ShareUrl!);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var invalidPin in new[] { "123", "12345", "123456", "12a4" })
        {
            var invalid = await publicClient.PostAsJsonAsync(
                $"/api/public/deliveries/{token}/deliver",
                new ConfirmDeliveryPinRequestDto { Pin = invalidPin });
            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.Status.Should().Be(OrderStatus.OnDelivery);
            stored.DeliveryConfirmedAtUtc.Should().BeNull("invalid input never confirms the delivery");
        }

        var valid = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = pin });
        valid.StatusCode.Should().Be(HttpStatusCode.OK, await valid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IndividualDeliveryLink_ShouldConfirmWithoutRoute_AndNeverExposeThePin()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("INDIVIDUAL");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Individual", "11999990044", "11999990045", isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var start = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = driver.Id
            });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var pin = (await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}"))!.DeliveryPin!;
        pin.Should().Be("8888", "the PIN is the last four digits of the customer's phone");

        var share = await sellerClient.PostAsync(
            $"/api/orders/store/{order.OrderId}/delivery-link",
            null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var shared = await share.Content.ReadFromJsonAsync<ShareIndividualDeliveryResultDto>();
        shared!.ShareUrl.Should().Contain("/entrega/");
        var token = ExtractRouteToken(shared.ShareUrl!);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var publicDetails = await publicClient.GetStringAsync($"/api/public/deliveries/{token}");
        publicDetails.Should().NotContain("deliveryPin", "the public link never exposes the PIN");
        publicDetails.Should().NotContain("\"pin\"", "the public link never exposes the PIN");

        var publicDelivery = await publicClient.GetFromJsonAsync<PublicDeliveryRouteResponseDto>(
            $"/api/public/deliveries/{token}");
        publicDelivery.Should().NotBeNull();
        publicDelivery!.TotalStops.Should().Be(1);
        publicDelivery.Stops.Should().ContainSingle();
        publicDelivery.Stops.Single().OrderCode.Should().Be(
            (await sellerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
                $"/api/orders/store/{order.OrderId}"))!.Code);
        publicDelivery.Stops.Single().CanDeliver.Should().BeTrue();

        var wrongPin = pin == "0000" ? "0001" : "0000";
        var wrong = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = wrongPin });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId))
                .Status.Should().Be(OrderStatus.OnDelivery, "a wrong PIN never concludes the order");
            (await dbContext.DeliveryRoutes.AsNoTracking().AnyAsync(x => x.StoreId == storeId))
                .Should().BeFalse("sharing an individual delivery never creates a route");
        }

        var confirm = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = pin });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        var confirmed = await confirm.Content.ReadFromJsonAsync<PublicDeliveryRoutePinResultDto>();
        confirmed!.Succeeded.Should().BeTrue();
        confirmed.OrderStatus.Should().Be(OrderStatus.Delivered);
        confirmed.DeliveryConfirmedAtUtc.Should().NotBeNull();
        confirmed.DeliveryConfirmedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);

        var repeated = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = pin });
        repeated.StatusCode.Should().Be(HttpStatusCode.OK, "repeating the confirmation is idempotent");

        var completed = await publicClient.GetFromJsonAsync<PublicDeliveryRouteResponseDto>(
            $"/api/public/deliveries/{token}");
        completed!.DeliveredStops.Should().Be(1);
        completed.Stops.Single().CanDeliver.Should().BeFalse();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.Status.Should().Be(OrderStatus.Delivered);
            stored.DeliveryConfirmedAtUtc.Should().NotBeNull();
            stored.DeliveryConfirmedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
            stored.DeliveryPinConfirmedByDriverId.Should().Be(driver.DeliveryDriverId);
            stored.DeliveryLinkTokenHash.Should().NotBe(token);
            stored.DeliveryLinkTokenHash.Should().HaveLength(64);
        }
    }

    [Fact]
    public async Task LegacySixDigitPin_ShouldBeReplacedWhenTheOrderEntersOnDelivery()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("LEGACY-PIN");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Motoboy Legado", "11999990046", null, isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        using (var seedScope = _factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var encryption = seedScope.ServiceProvider.GetRequiredService<IEncryptionService>();
            var stored = await dbContext.Orders.SingleAsync(x => x.Id == order.OrderId);
            stored.DeliveryPinHash = new PasswordHasher<Order>().HashPassword(stored, "123456");
            stored.DeliveryPinEncrypted = encryption.Encrypt("123456");
            await dbContext.SaveChangesAsync();
        }

        var start = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = driver.Id
            });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var details = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        details!.DeliveryPin.Should().MatchRegex(@"^\d{4}$");
        details.DeliveryPin.Should().Be("8888", "the legacy PIN is replaced by the customer's phone suffix");

        var share = await sellerClient.PostAsync(
            $"/api/orders/store/{order.OrderId}/delivery-link",
            null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var token = ExtractRouteToken(
            (await share.Content.ReadFromJsonAsync<ShareIndividualDeliveryResultDto>())!.ShareUrl!);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var legacyConfirm = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = "123456" });
        legacyConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the public contract only accepts the current 4-digit format");

        var currentConfirm = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = details.DeliveryPin! });
        currentConfirm.StatusCode.Should().Be(HttpStatusCode.OK, await currentConfirm.Content.ReadAsStringAsync());
    }

    private async Task SetCustomerPhoneAsync(Guid orderId, string? phoneNumber)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var customerUserId = await dbContext.Orders
            .AsNoTracking()
            .Where(x => x.Id == orderId)
            .Select(x => x.CustomerUserId)
            .SingleAsync();
        var customer = await dbContext.Users.SingleAsync(x => x.Id == customerUserId);
        customer.PhoneNumber = phoneNumber;
        await dbContext.SaveChangesAsync();
    }

    private static async Task<SellerDeliveryDriverResponseDto> CreateSimpleDriverAsync(
        HttpClient sellerClient,
        string name,
        string phone,
        string? whatsApp,
        bool isActive)
    {
        var response = await sellerClient.PostAsJsonAsync(
            "/api/delivery-drivers",
            new UpsertStoreDeliveryDriverRequestDto
            {
                Name = name,
                PhoneNumber = phone,
                WhatsAppPhoneNumber = whatsApp,
                IsActive = isActive
            });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var driver = await response.Content.ReadFromJsonAsync<SellerDeliveryDriverResponseDto>();
        driver.Should().NotBeNull();
        return driver!;
    }

    private static async Task StartDeliveryAsync(
        HttpClient sellerClient,
        Guid orderId,
        Guid storeDeliveryDriverId)
    {
        var response = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.OnDelivery,
                DeliveryDriverId = storeDeliveryDriverId
            });
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    private static string ExtractRouteToken(string shareUrl)
    {
        var uri = new Uri(shareUrl);
        return Uri.UnescapeDataString(uri.Segments[^1].Trim('/'));
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var propertyName in new[] { "detail", "error" })
        {
            if (document.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
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
        var response = await sellerClient.PostAsJsonAsync("/api/delivery-drivers/store/invite", new InviteDeliveryDriverRequestDto
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
