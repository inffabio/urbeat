using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
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
    public async Task Assignment_ShouldRejectCrossStoreDriver_AndKeepSellerOwnership()
    {
        var (sellerAClient, storeAId) = await CreateStoreAsync("A");
        var (sellerBClient, storeBId) = await CreateStoreAsync("B");

        var driverA = await CreateSimpleDriverAsync(
            sellerAClient, "Entregador A", "11999990001", null, isActive: true);

        var orderA = await CreateDeliveryOrderAsync(sellerAClient, storeAId);
        var orderB = await CreateDeliveryOrderAsync(sellerBClient, storeBId);

        var crossStore = await sellerBClient.PutAsJsonAsync(
            $"/api/orders/store/{orderB.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driverA.Id });
        crossStore.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var assignA = await sellerAClient.PutAsJsonAsync(
            $"/api/orders/store/{orderA.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driverA.Id });
        assignA.StatusCode.Should().Be(HttpStatusCode.OK, await assignA.Content.ReadAsStringAsync());
        var assigned = await assignA.Content.ReadFromJsonAsync<OrderDetailsResponseDto>();
        assigned!.DeliveryDriverName.Should().Be("Entregador A");
        assigned.DeliveryDriverId.Should().Be(driverA.DeliveryDriverId);

        var driverB = await CreateSimpleDriverAsync(
            sellerBClient, "Entregador B", "11999990002", null, isActive: true);
        var otherStore = await sellerAClient.PutAsJsonAsync(
            $"/api/orders/store/{orderA.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driverB.Id });
        otherStore.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deactivate_ShouldBeBlocked_WhileAssignedOrderStillNeedsPublicLinkToConfirmPin()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Entregador Inativacao", "11999990001", null, isActive: true);

        var order = await CreateDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        var customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        customerDetails!.DeliveryPin.Should().BeNull("the PIN must not be exposed before OnDelivery");

        await StartDeliveryAsync(sellerClient, order.OrderId, driver.Id);

        customerDetails = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        customerDetails!.DeliveryPin.Should().NotBeNullOrWhiteSpace();

        var deactivateOnDelivery = await sellerClient.PatchAsJsonAsync(
            $"/api/delivery-drivers/{driver.Id}/status",
            new SetStoreDeliveryDriverStatusRequestDto { IsActive = false });
        deactivateOnDelivery.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await deactivateOnDelivery.Content.ReadAsStringAsync()).Should().Contain("PIN");

        var share = await sellerClient.PostAsync(
            $"/api/orders/store/{order.OrderId}/delivery-link",
            null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var token = ExtractRouteToken(
            (await share.Content.ReadFromJsonAsync<ShareIndividualDeliveryResultDto>())!.ShareUrl!);

        var markDelivered = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Delivered });
        markDelivered.StatusCode.Should().Be(HttpStatusCode.OK, await markDelivered.Content.ReadAsStringAsync());

        var deactivateAwaitingPin = await sellerClient.PatchAsJsonAsync(
            $"/api/delivery-drivers/{driver.Id}/status",
            new SetStoreDeliveryDriverStatusRequestDto { IsActive = false });
        deactivateAwaitingPin.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "delivered orders without PIN confirmation still depend on the assigned driver link");

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var confirm = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = customerDetails.DeliveryPin! });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        var deactivateAfterPin = await sellerClient.PatchAsJsonAsync(
            $"/api/delivery-drivers/{driver.Id}/status",
            new SetStoreDeliveryDriverStatusRequestDto { IsActive = false });
        deactivateAfterPin.StatusCode.Should().Be(HttpStatusCode.OK, await deactivateAfterPin.Content.ReadAsStringAsync());
    }




    [Fact]
    public async Task PickupOrder_ShouldNotAcceptDeliveryDriverAssignment()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("A");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Entregador Pickup", "11999990001", null, isActive: true);

        var pickupOrderId = await CreateOrderAsync(sellerClient, storeId, FulfillmentType.PickUp, null);

        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{pickupOrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task StoreDriverCrud_ShouldWorkWithOperationalFieldsOnly_AndExposeWhatsApp()
    {
        var (sellerClient, _) = await CreateStoreAsync("CRUD");

        var created = await CreateSimpleDriverAsync(
            sellerClient,
            "Motoboy Simples",
            "(11) 99999-0011",
            "(11) 99999-0012",
            isActive: true);

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





    [Fact]
    public async Task PublicLink_ShouldNotPersistHandoverPayment_WhenPinIsWrongOrLocked()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("PUBLIC-ATOMIC");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Entregador Publico", "11999990001", null, isActive: true);

        var order = await CreateNewContractDeliveryOrderAsync(
            sellerClient,
            storeId,
            PaymentMethod.CashOnDelivery,
            cashWithoutChange: false,
            cashChangeForAmount: 50m);

        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        await StartDeliveryAsync(sellerClient, order.OrderId, driver.Id);

        var pin = (await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}"))!.DeliveryPin!;
        var wrongPin = pin == "0000" ? "0001" : "0000";

        var share = await sellerClient.PostAsync(
            $"/api/orders/store/{order.OrderId}/delivery-link",
            null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var token = ExtractRouteToken(
            (await share.Content.ReadFromJsonAsync<ShareIndividualDeliveryResultDto>())!.ShareUrl!);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var wrong = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto
            {
                Pin = wrongPin,
                HandoverConfirmed = true,
                CashReceivedAmount = 30m
            });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.PaymentConfirmedAtUtc.Should().BeNull("a wrong PIN must never confirm the payment");
            stored.CashReceivedAmount.Should().BeNull();
            stored.CashChangeAmount.Should().BeNull();

            var assignment = await dbContext.OrderDeliveryAssignments.AsNoTracking()
                .SingleAsync(x => x.OrderId == order.OrderId);
            assignment.HandoverConfirmedAtUtc.Should().BeNull();
        }

        // Exhaust the PIN attempts; a locked PIN must also leave the payment untouched.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await publicClient.PostAsJsonAsync(
                $"/api/public/deliveries/{token}/deliver",
                new ConfirmDeliveryPinRequestDto
                {
                    Pin = wrongPin,
                    HandoverConfirmed = true,
                    CashReceivedAmount = 30m
                });
        }

        var locked = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto
            {
                Pin = pin,
                HandoverConfirmed = true,
                CashReceivedAmount = 30m
            });
        locked.StatusCode.Should().Be(HttpStatusCode.Locked);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.PaymentConfirmedAtUtc.Should().BeNull("a locked PIN must never confirm the payment");
            stored.CashReceivedAmount.Should().BeNull();
            stored.CashChangeAmount.Should().BeNull();

            var assignment = await dbContext.OrderDeliveryAssignments.AsNoTracking()
                .SingleAsync(x => x.OrderId == order.OrderId);
            assignment.HandoverConfirmedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task SellerDelivered_ShouldNotAllowCustomerConfirmationToBypassHandoverAndPin()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("BYPASS");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Entregador Bypass", "11999990001", null, isActive: true);

        var order = await CreateNewContractDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        await StartDeliveryAsync(sellerClient, order.OrderId, driver.Id);

        var detailsBeforeDelivered = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        detailsBeforeDelivered!.DeliveryPin.Should().MatchRegex(@"^\d{4}$");

        var share = await sellerClient.PostAsync(
            $"/api/orders/store/{order.OrderId}/delivery-link",
            null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var token = ExtractRouteToken(
            (await share.Content.ReadFromJsonAsync<ShareIndividualDeliveryResultDto>())!.ShareUrl!);

        // A seller can mark the order Delivered, but that must not finish the delivery.
        var sellerDelivered = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order.OrderId}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Delivered });
        sellerDelivered.StatusCode.Should().Be(HttpStatusCode.OK, await sellerDelivered.Content.ReadAsStringAsync());

        var customerBypass = await order.CustomerClient.PostAsync(
            $"/api/orders/{order.OrderId}/delivery-confirmation",
            null);
        customerBypass.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await customerBypass.Content.ReadAsStringAsync()).Should().Contain("PIN");

        var completeBeforePin = await sellerClient.PostAsync($"/api/orders/{order.OrderId}/complete", null);
        completeBeforePin.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.DeliveryConfirmedAtUtc.Should().BeNull();
            stored.DeliveryPinConfirmedAtUtc.Should().BeNull();
        }

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var pin = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto
            {
                Pin = detailsBeforeDelivered.DeliveryPin!,
                HandoverConfirmed = true
            });
        pin.StatusCode.Should().Be(HttpStatusCode.OK, await pin.Content.ReadAsStringAsync());

        var completeAfterPin = await sellerClient.PostAsync($"/api/orders/{order.OrderId}/complete", null);
        completeAfterPin.StatusCode.Should().Be(HttpStatusCode.OK, await completeAfterPin.Content.ReadAsStringAsync());
    }

    private async Task<(Guid OrderId, HttpClient CustomerClient)> CreateNewContractDeliveryOrderAsync(
        HttpClient sellerClient,
        Guid storeId,
        PaymentMethod paymentMethod = PaymentMethod.PixOnDelivery,
        bool? cashWithoutChange = null,
        decimal? cashChangeForAmount = null)
    {
        var customerClient = await CreateCustomerClientAsync();
        var productId = await ProductTestHelper.CreateProductAsync(
            sellerClient, storeId, $"Produto {Guid.NewGuid():N}", 20m);
        var addressId = await CreateAddressAsync(customerClient);

        var response = await customerClient.PostAsJsonAsync("/api/orders", new CheckoutRequestDto
        {
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            CustomerAddressId = addressId,
            PaymentMethod = paymentMethod,
            CashWithoutChange = cashWithoutChange,
            CashChangeForAmount = cashChangeForAmount,
            Items =
            [
                new CheckoutItemRequestDto { ProductId = productId, Quantity = 1 }
            ]
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();

        await AdvanceToReadyAsync(sellerClient, created!.OrderId);
        return (created.OrderId, customerClient);
    }

    [Fact]
    public async Task PublicLink_ShouldRequireHandoverBeforePin_AndPersistPayment()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("HANDOVER");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Entregador Handover", "11999990001", null, isActive: true);

        var order = await CreateNewContractDeliveryOrderAsync(sellerClient, storeId);
        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        await StartDeliveryAsync(sellerClient, order.OrderId, driver.Id);

        var details = await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}");
        details!.DeliveryPin.Should().MatchRegex(@"^\d{4}$");

        var share = await sellerClient.PostAsync(
            $"/api/orders/store/{order.OrderId}/delivery-link",
            null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var token = ExtractRouteToken(
            (await share.Content.ReadFromJsonAsync<ShareIndividualDeliveryResultDto>())!.ShareUrl!);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var pinBeforeHandover = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto { Pin = details.DeliveryPin! });
        pinBeforeHandover.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await pinBeforeHandover.Content.ReadAsStringAsync()).Should().Contain("recebimento");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignment = await db.OrderDeliveryAssignments.AsNoTracking()
                .SingleAsync(x => x.OrderId == order.OrderId);
            assignment.PinAttemptCount.Should().Be(0, "a missing handover is not a wrong PIN");
        }

        var confirm = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto
            {
                Pin = details.DeliveryPin!,
                HandoverConfirmed = true
            });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.Status.Should().Be(OrderStatus.Delivered);
            stored.PaymentConfirmedAtUtc.Should().NotBeNull();
            stored.PaymentConfirmedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
            stored.PaymentConfirmedByDriverId.Should().BeNull(
                "the public link has no authenticated driver identity");
        }
    }

    [Fact]
    public async Task PublicLink_ShouldValidateCashReceivedAndComputeChange()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("CASH");
        var driver = await CreateSimpleDriverAsync(
            sellerClient, "Entregador Dinheiro", "11999990001", null, isActive: true);

        var order = await CreateNewContractDeliveryOrderAsync(
            sellerClient,
            storeId,
            PaymentMethod.CashOnDelivery,
            cashWithoutChange: false,
            cashChangeForAmount: 50m);

        var assign = await sellerClient.PutAsJsonAsync(
            $"/api/orders/store/{order.OrderId}/delivery-driver",
            new AssignDeliveryDriverRequestDto { DeliveryDriverId = driver.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

        await StartDeliveryAsync(sellerClient, order.OrderId, driver.Id);

        var pin = (await order.CustomerClient.GetFromJsonAsync<OrderDetailsResponseDto>(
            $"/api/orders/{order.OrderId}"))!.DeliveryPin!;

        var share = await sellerClient.PostAsync(
            $"/api/orders/store/{order.OrderId}/delivery-link",
            null);
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        var token = ExtractRouteToken(
            (await share.Content.ReadFromJsonAsync<ShareIndividualDeliveryResultDto>())!.ShareUrl!);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var below = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto
            {
                Pin = pin,
                HandoverConfirmed = true,
                CashReceivedAmount = 10m
            });
        below.StatusCode.Should().Be(HttpStatusCode.BadRequest, await below.Content.ReadAsStringAsync());

        var success = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto
            {
                Pin = pin,
                HandoverConfirmed = true,
                CashReceivedAmount = 60m
            });
        success.StatusCode.Should().Be(HttpStatusCode.OK, await success.Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.CashReceivedAmount.Should().Be(60m);
            stored.CashChangeAmount.Should().Be(36m,
                "the checkout forecast is not a cap and the server computes the real change");
        }

        var repeat = await publicClient.PostAsJsonAsync(
            $"/api/public/deliveries/{token}/deliver",
            new ConfirmDeliveryPinRequestDto
            {
                Pin = pin,
                HandoverConfirmed = true,
                CashReceivedAmount = 30m
            });
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, "repeating a confirmed delivery is idempotent");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.OrderId);
            stored.CashReceivedAmount.Should().Be(60m, "repeating never rewrites the collected amount");
            stored.CashChangeAmount.Should().Be(36m);
        }
    }

    [Fact]
    public async Task Pickup_ShouldBeCompletedWithoutDriverOrPin()
    {
        var (sellerClient, storeId) = await CreateStoreAsync("PICKUP");
        var order = await CreateOrderAsync(
            sellerClient,
            storeId,
            FulfillmentType.PickUp,
            addressId: null);

        foreach (var status in new[] { OrderStatus.Preparing, OrderStatus.Ready })
        {
            var response = await sellerClient.PatchAsJsonAsync(
                $"/api/orders/{order}/status",
                new UpdateOrderStatusRequestDto { NewStatus = status });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        var delivered = await sellerClient.PatchAsJsonAsync(
            $"/api/orders/{order}/status",
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Delivered });
        delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());

        var complete = await sellerClient.PostAsync($"/api/orders/{order}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == order);
        stored.Status.Should().Be(OrderStatus.Delivered);
        stored.SellerCompletedAtUtc.Should().NotBeNull();
        (await db.OrderDeliveryAssignments.AsNoTracking().AnyAsync(x => x.OrderId == order))
            .Should().BeFalse("pickup must never require a motoboy or delivery PIN");
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
            PaymentMethod = PaymentMethod.PixOnDelivery,
            Items =
            [
                new CheckoutItemRequestDto { ProductId = productId.Value, Quantity = 1 }
            ]
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<CheckoutConfirmResponseDto>();

        // These PIN/route tests predate the manual handover contract and exercise the legacy flow.
        // New-contract handover behavior has its own dedicated tests below.
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await dbContext.Orders.SingleAsync(x => x.Id == created!.OrderId);
            order.PaymentMethod = PaymentMethod.CardOnDelivery;
            await dbContext.SaveChangesAsync();
        }

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
