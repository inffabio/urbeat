using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class OrderServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-orders-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        _sut = new OrderService(
            _db,
            new EfUnitOfWork(_db),
            Mock.Of<INotificationService>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task GetStoreOrderAsync_ShouldReturnCustomerContactAndCompleteItemComposition()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = sellerUserId,
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999"
        };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Status = OrderStatus.Received,
            Subtotal = 35m,
            DeliveryFee = 7.5m,
            Total = 42.5m
        };
        _db.Users.Add(new IdentityUser<Guid>
        {
            Id = customerUserId,
            UserName = "cliente@teste.com",
            Email = "cliente@teste.com",
            PhoneNumber = "11988887777"
        });
        _db.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = customerUserId,
            ClaimType = "FullName",
            ClaimValue = "Cliente Teste"
        });
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        _db.OrderItems.Add(new OrderItem
        {
            OrderId = order.Id,
            ProductName = "Pizza grande",
            Quantity = 1,
            UnitPrice = 35m,
            TotalPrice = 35m,
            Notes = "Sem cebola",
            VariationName = "Grande",
            WeightGrams = 500,
            ChoiceOptionName = "Meio a meio",
            AdditionalNames = "Borda recheada, Bacon"
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreOrderAsync(sellerUserId, order.Id);

        result.Should().NotBeNull();
        result!.CustomerName.Should().Be("Cliente Teste");
        result.CustomerPhoneNumber.Should().Be("11988887777");
        var item = result.Items.Should().ContainSingle().Subject;
        item.Notes.Should().Be("Sem cebola");
        item.VariationName.Should().Be("Grande");
        item.WeightGrams.Should().Be(500);
        item.ChoiceOptionName.Should().Be("Meio a meio");
        item.AdditionalNames.Should().Be("Borda recheada, Bacon");
    }

    [Fact]
    public async Task ListStoreOrdersAsync_ShouldReturnSellerOrderSummaryWithOperationalDetails()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = sellerUserId,
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999"
        };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Status = OrderStatus.Received,
            Subtotal = 35m,
            DeliveryFee = 7.5m,
            Total = 42.5m,
            AddressStreet = "Rua Teste",
            AddressNumber = "10",
            AddressNeighborhood = "Centro"
        };
        _db.Users.Add(new IdentityUser<Guid>
        {
            Id = customerUserId,
            UserName = "cliente@teste.com",
            Email = "cliente@teste.com",
            PhoneNumber = "11988887777"
        });
        _db.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = customerUserId,
            ClaimType = "FullName",
            ClaimValue = "Cliente Teste"
        });
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        _db.OrderItems.Add(new OrderItem
        {
            OrderId = order.Id,
            ProductName = "Pizza grande",
            Quantity = 2,
            UnitPrice = 35m,
            TotalPrice = 70m
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ListStoreOrdersAsync(sellerUserId, new StoreOrdersHistoryQueryDto { PageSize = 10 });

        var summary = result.Items.Should().ContainSingle().Subject;
        summary.CustomerName.Should().Be("Cliente Teste");
        summary.CustomerPhoneNumber.Should().Be("11988887777");
        summary.FulfillmentType.Should().Be(FulfillmentType.Delivery);
        summary.PaymentMethod.Should().Be(PaymentMethod.CashOnDelivery);
        summary.AddressSummary.Should().Be("Rua Teste, 10 - Centro");
        summary.ItemsSummary.Should().Be("2x Pizza grande");
    }

    [Fact]
    public async Task ListStoreCustomersAsync_ShouldReturnPaginatedCustomersAndMetricsFromSellerOrders()
    {
        var sellerUserId = Guid.NewGuid();
        var otherSellerUserId = Guid.NewGuid();
        var firstCustomerUserId = Guid.NewGuid();
        var secondCustomerUserId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var otherStore = new Store { OwnerUserId = otherSellerUserId, Name = "Outra Loja", Slug = "outra-loja", PhoneNumber = "11888888888" };
        var firstCustomerRecentOrder = new Order { CustomerUserId = firstCustomerUserId, StoreId = store.Id, Status = OrderStatus.Delivered, Total = 30m };
        SetCreatedAtUtc(firstCustomerRecentOrder, now.AddDays(-3));
        var firstCustomerLastOrder = new Order { CustomerUserId = firstCustomerUserId, StoreId = store.Id, Status = OrderStatus.Received, Total = 50m };
        SetCreatedAtUtc(firstCustomerLastOrder, now.AddDays(-1));
        var secondCustomerOrder = new Order { CustomerUserId = secondCustomerUserId, StoreId = store.Id, Status = OrderStatus.Delivered, Total = 90m };
        SetCreatedAtUtc(secondCustomerOrder, now.AddDays(-45));
        var otherStoreOrder = new Order { CustomerUserId = firstCustomerUserId, StoreId = otherStore.Id, Status = OrderStatus.Delivered, Total = 120m };
        SetCreatedAtUtc(otherStoreOrder, now.AddDays(-2));
        _db.Users.AddRange(
            new IdentityUser<Guid> { Id = firstCustomerUserId, UserName = "cliente1@teste.com", Email = "cliente1@teste.com", PhoneNumber = "11988887777" },
            new IdentityUser<Guid> { Id = secondCustomerUserId, UserName = "cliente2@teste.com", Email = "cliente2@teste.com", PhoneNumber = "11977776666" });
        _db.UserClaims.AddRange(
            new IdentityUserClaim<Guid> { UserId = firstCustomerUserId, ClaimType = "FullName", ClaimValue = "Cliente Recente" },
            new IdentityUserClaim<Guid> { UserId = secondCustomerUserId, ClaimType = "FullName", ClaimValue = "Cliente Antigo" });
        _db.Stores.AddRange(store, otherStore);
        _db.Orders.AddRange(firstCustomerRecentOrder, firstCustomerLastOrder, secondCustomerOrder, otherStoreOrder);
        await _db.SaveChangesAsync();

        var result = await _sut.ListStoreCustomersAsync(sellerUserId, new StoreCustomersQueryDto
        {
            Page = 2,
            PageSize = 1,
            Sort = "totalSpentDesc"
        });

        result.Page.Should().Be(2);
        result.PageSize.Should().Be(1);
        result.TotalItems.Should().Be(2);
        result.TotalPages.Should().Be(2);
        result.Metrics.TotalCustomers.Should().Be(2);
        result.Metrics.ActiveCustomers.Should().Be(1);
        result.Metrics.RecurringCustomers.Should().Be(0);
        result.Metrics.NewCustomersThisMonth.Should().Be(1);
        result.Metrics.AverageTicket.Should().Be(85m);

        var customer = result.Items.Should().ContainSingle().Subject;
        customer.Id.Should().Be(firstCustomerUserId.ToString());
        customer.Name.Should().Be("Cliente Recente");
        customer.Email.Should().Be("cliente1@teste.com");
        customer.Phone.Should().Be("11988887777");
        customer.TotalOrders.Should().Be(2);
        customer.TotalSpent.Should().Be(80m);
        customer.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ListStoreCustomersAsync_ShouldIncludeRegisteredCustomerWithoutOrders()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };

        _db.Users.Add(new IdentityUser<Guid>
        {
            Id = customerUserId,
            UserName = "cliente@teste.com",
            Email = "cliente@teste.com",
            PhoneNumber = "11988887777"
        });
        _db.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = customerUserId,
            ClaimType = "FullName",
            ClaimValue = "Cliente cadastrado"
        });
        _db.Stores.Add(store);
        _db.StoreCustomers.Add(new StoreCustomer
        {
            StoreId = store.Id,
            CustomerUserId = customerUserId,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ListStoreCustomersAsync(sellerUserId, new StoreCustomersQueryDto());

        var customer = result.Items.Should().ContainSingle().Subject;
        customer.Id.Should().Be(customerUserId.ToString());
        customer.Name.Should().Be("Cliente cadastrado");
        customer.TotalOrders.Should().Be(0);
        customer.TotalSpent.Should().Be(0);
        customer.LastOrderAtUtc.Should().BeNull();
    }

    private static void SetCreatedAtUtc(BaseEntity entity, DateTime value)
    {
        typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.CreatedAtUtc))!
            .SetValue(entity, value);
    }

    [Fact]
    public async Task ListStoreDeliveriesAsync_ShouldReturnDeliveryOrdersForSellerStoreOnly()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        _db.Users.Add(new IdentityUser<Guid> { Id = customerUserId, UserName = "cliente@teste.com", Email = "cliente@teste.com", PhoneNumber = "11988887777" });
        _db.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = customerUserId, ClaimType = "FullName", ClaimValue = "Cliente Teste" });
        _db.Stores.Add(store);
        _db.Orders.AddRange(
            new Order { Code = "123", CustomerUserId = customerUserId, StoreId = store.Id, FulfillmentType = FulfillmentType.Delivery, Status = OrderStatus.OnDelivery, Total = 40m, AddressStreet = "Rua Teste", AddressNumber = "10", AddressNeighborhood = "Centro" },
            new Order { Code = "124", CustomerUserId = customerUserId, StoreId = store.Id, FulfillmentType = FulfillmentType.PickUp, Status = OrderStatus.Ready, Total = 25m });
        await _db.SaveChangesAsync();

        var result = await _sut.ListStoreDeliveriesAsync(sellerUserId);

        var delivery = result.Should().ContainSingle().Subject;
        delivery.Code.Should().Be("123");
        delivery.CustomerName.Should().Be("Cliente Teste");
        delivery.CustomerPhoneNumber.Should().Be("11988887777");
        delivery.AddressSummary.Should().Be("Rua Teste, 10 - Centro");
        delivery.Status.Should().Be(OrderStatus.OnDelivery);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldReturnOrderWithNullConfirmationTimestamps()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Status = OrderStatus.Received,
            Subtotal = 35m,
            DeliveryFee = 7.5m,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateStatusAsync(
            sellerUserId,
            order.Id,
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Preparing },
            null);

        result.Order.Should().NotBeNull();
        result.Order!.DeliveryConfirmedAtUtc.Should().BeNull();
        result.Order.SellerCompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetStoreOrderAsync_ShouldPersistDeliveryConfirmedAtUtc()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var confirmedAtUtc = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.Delivered,
            Total = 42.5m,
            DeliveryConfirmedAtUtc = confirmedAtUtc
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreOrderAsync(sellerUserId, order.Id);

        result.Should().NotBeNull();
        result!.DeliveryConfirmedAtUtc.Should().Be(confirmedAtUtc);
        result.SellerCompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetStoreOrderAsync_ShouldPersistSellerCompletedAtUtc()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var completedAtUtc = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.Delivered,
            Total = 42.5m,
            SellerCompletedAtUtc = completedAtUtc
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _sut.GetStoreOrderAsync(sellerUserId, order.Id);

        result.Should().NotBeNull();
        result!.SellerCompletedAtUtc.Should().Be(completedAtUtc);
        result.DeliveryConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmDeliveryAsync_ShouldSucceedAndBeIdempotent_ForDeliveredDeliveryOrder()
    {
        var customerUserId = Guid.NewGuid();
        var sellerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.Delivered,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var first = await _sut.ConfirmDeliveryAsync(customerUserId, order.Id, "127.0.0.1");
        var second = await _sut.ConfirmDeliveryAsync(customerUserId, order.Id, "127.0.0.1");

        first.Order.Should().NotBeNull();
        first.Order!.DeliveryConfirmedAtUtc.Should().NotBeNull();
        first.Order.SellerCompletedAtUtc.Should().BeNull();
        second.Order.Should().NotBeNull();
        second.Order!.DeliveryConfirmedAtUtc.Should().Be(first.Order.DeliveryConfirmedAtUtc);
        (await _db.AuditLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ConfirmDeliveryAsync_ShouldReject_DeliveredPickUpOrder()
    {
        var customerUserId = Guid.NewGuid();
        var sellerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.PickUp,
            Status = OrderStatus.Delivered,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _sut.ConfirmDeliveryAsync(customerUserId, order.Id, null);

        result.InvalidState.Should().BeTrue();
        result.Order.Should().BeNull();
        (await _db.Orders.SingleAsync()).DeliveryConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmDeliveryAsync_ShouldReject_NonDeliveredOrder()
    {
        var customerUserId = Guid.NewGuid();
        var sellerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.OnDelivery,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _sut.ConfirmDeliveryAsync(customerUserId, order.Id, null);

        result.InvalidState.Should().BeTrue();
        result.Order.Should().BeNull();
        (await _db.Orders.SingleAsync()).DeliveryConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmDeliveryAsync_ShouldForbid_DifferentCustomer()
    {
        var customerUserId = Guid.NewGuid();
        var otherCustomerUserId = Guid.NewGuid();
        var sellerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.Delivered,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _sut.ConfirmDeliveryAsync(otherCustomerUserId, order.Id, null);

        result.Forbidden.Should().BeTrue();
        result.Order.Should().BeNull();
        (await _db.Orders.SingleAsync()).DeliveryConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CompleteForSellerBoardAsync_ShouldSetSellerCompletedAtUtc_WithoutChangingStatus()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.Delivered,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var first = await _sut.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1");
        var second = await _sut.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1");

        first.Order.Should().NotBeNull();
        first.Order!.SellerCompletedAtUtc.Should().NotBeNull();
        first.Order.Status.Should().Be(OrderStatus.Delivered);
        second.Order.Should().NotBeNull();
        second.Order!.SellerCompletedAtUtc.Should().Be(first.Order.SellerCompletedAtUtc);
        (await _db.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Delivered);
        (await _db.AuditLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldEmitOrderStatusUpdatedEvent_AfterValidTransition()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "URB-123456",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.Received,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var notificationService = new Mock<INotificationService>();
        DateTime capturedChangedAtUtc = default;
        notificationService
            .Setup(x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, OrderStatus, DateTime, CancellationToken>(
                (_, _, _, _, changedAtUtc, _) => capturedChangedAtUtc = changedAtUtc)
            .Returns(Task.CompletedTask);

        var sut = new OrderService(_db, new EfUnitOfWork(_db), notificationService.Object);

        var before = DateTime.UtcNow;
        var result = await sut.UpdateStatusAsync(
            sellerUserId,
            order.Id,
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Preparing },
            null);
        var after = DateTime.UtcNow;

        result.InvalidTransition.Should().BeFalse();
        result.Order.Should().NotBeNull();
        notificationService.Verify(
            x => x.NotifyCustomerOrderStatusUpdatedAsync(
                customerUserId,
                order.Id,
                "URB-123456",
                OrderStatus.Preparing,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        capturedChangedAtUtc.Should().BeOnOrAfter(before);
        capturedChangedAtUtc.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldNotEmitOrderStatusUpdatedEvent_OnInvalidTransition()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "URB-123456",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.Received,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var notificationService = new Mock<INotificationService>();
        var sut = new OrderService(_db, new EfUnitOfWork(_db), notificationService.Object);

        var result = await sut.UpdateStatusAsync(
            sellerUserId,
            order.Id,
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Delivered },
            null);

        result.InvalidTransition.Should().BeTrue();
        result.Order.Should().BeNull();
        notificationService.Verify(
            x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteForSellerBoardAsync_ShouldForbid_DifferentSeller()
    {
        var sellerUserId = Guid.NewGuid();
        var otherSellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja Teste", Slug = "loja-teste", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "123",
            CustomerUserId = customerUserId,
            StoreId = store.Id,
            FulfillmentType = FulfillmentType.Delivery,
            Status = OrderStatus.Delivered,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _sut.CompleteForSellerBoardAsync(otherSellerUserId, order.Id, null);

        result.Forbidden.Should().BeTrue();
        result.Order.Should().BeNull();
        (await _db.Orders.SingleAsync()).SellerCompletedAtUtc.Should().BeNull();
    }
}
