using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class OrderServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApplicationDbContext _db;
    private readonly OrderService _sut;
    private readonly string _dbName;

    public OrderServiceTests()
    {
        _dbName = $"urbeat-orders-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        _db = new ApplicationDbContext(options);

        _sut = new OrderService(
            _db,
            new EfUnitOfWork(_db),
            Mock.Of<INotificationService>(),
            Mock.Of<IOutboxWriter>());
    }

    private (ApplicationDbContext Db, OrderService Service) CreateServiceWithFreshContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        var db = new ApplicationDbContext(options);
        var service = new OrderService(
            db,
            new EfUnitOfWork(db),
            Mock.Of<INotificationService>(),
            Mock.Of<IOutboxWriter>());
        return (db, service);
    }

    private static OrderService CreateService(ApplicationDbContext db)
    {
        return new OrderService(
            db,
            new EfUnitOfWork(db),
            Mock.Of<INotificationService>(),
            new OutboxWriter(db));
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
            AdditionalNames = "Borda recheada, Bacon",
            OptionPricesJson = "[{\"Name\":\"Borda recheada\",\"Price\":4.50},{\"Name\":\"Bacon\",\"Price\":5.00}]"
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
        item.OptionPrices.Should().HaveCount(2);
        item.OptionPrices.Should().Contain(x => x.Name == "Borda recheada" && x.Price == 4.50m);
        item.OptionPrices.Should().Contain(x => x.Name == "Bacon" && x.Price == 5.00m);
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
        summary.Subtotal.Should().Be(35m);
        summary.DeliveryFee.Should().Be(7.5m);
        summary.Total.Should().Be(42.5m);
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
            DeliveryConfirmedAtUtc = DateTime.UtcNow,
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
    public async Task CompleteForSellerBoardAsync_ShouldCreateSingleAudit_WhenCalledConcurrently()
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
            DeliveryConfirmedAtUtc = DateTime.UtcNow,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var (dbA, serviceA) = CreateServiceWithFreshContext();
        var (dbB, serviceB) = CreateServiceWithFreshContext();
        using (dbA)
        using (dbB)
        {
            var results = await Task.WhenAll(
                serviceA.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1"),
                serviceB.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1"));

            results.Should().OnlyContain(r => r.Order != null && r.Order!.SellerCompletedAtUtc.HasValue);
            results.Should().OnlyContain(r => !r.InvalidState && !r.NotFound && !r.Forbidden);

            var verifyOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(_dbName)
                .Options;
            using var verify = new ApplicationDbContext(verifyOptions);
            (await verify.AuditLogs.CountAsync()).Should().Be(1);
            (await verify.Orders.SingleAsync()).SellerCompletedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task CompleteForSellerBoardAsync_ShouldPersistOrderAndAudit_InSingleSaveChanges()
    {
        // The InMemory provider has no real database transactions, so atomicity is verified
        // unitarily by asserting the order update and the audit log are committed together in a
        // single SaveChanges call (the unit EF Core wraps in one transaction).
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
            DeliveryConfirmedAtUtc = DateTime.UtcNow,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var uow = new Mock<IEfUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new OrderService(_db, uow.Object, Mock.Of<INotificationService>(), Mock.Of<IOutboxWriter>());

        var result = await sut.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1");

        result.Order.Should().NotBeNull();
        result.Order!.SellerCompletedAtUtc.Should().NotBeNull();
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteForSellerBoardAsync_ShouldKeepSellerCompletedAtUtcNull_WhenAuditSaveFails()
    {
        // Simulates a failure in the single SaveChanges (e.g. the audit insert). The rollback must
        // leave the order untouched. Real PostgreSQL transaction rollback is not available in the
        // InMemory test provider, so this is covered unitarily with a failing unit-of-work.
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
            DeliveryConfirmedAtUtc = DateTime.UtcNow,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var uow = new Mock<IEfUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated audit insert failure."));

        var sut = new OrderService(_db, uow.Object, Mock.Of<INotificationService>(), Mock.Of<IOutboxWriter>());

        Func<Task> act = () => sut.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1");

        await act.Should().ThrowAsync<DbUpdateException>();

        using var verify = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);
        (await verify.Orders.SingleAsync()).SellerCompletedAtUtc.Should().BeNull();
        (await verify.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldEnqueueOrderStatusChangedEvent_AfterValidTransition()
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

        var sut = new OrderService(_db, new EfUnitOfWork(_db), Mock.Of<INotificationService>(), new OutboxWriter(_db));

        var before = DateTime.UtcNow;
        var result = await sut.UpdateStatusAsync(
            sellerUserId,
            order.Id,
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Preparing },
            null);
        var after = DateTime.UtcNow;

        result.InvalidTransition.Should().BeFalse();
        result.Order.Should().NotBeNull();

        var message = _db.OutboxMessages.Single(x => x.Type == OutboxEventTypes.OrderStatusChanged);
        message.AggregateId.Should().Be(order.Id);
        var payload = JsonSerializer.Deserialize<OrderStatusChangedEvent>(message.Payload, JsonOptions);
        payload!.PreviousStatus.Should().Be(OrderStatus.Received);
        payload.NewStatus.Should().Be(OrderStatus.Preparing);
        payload.Source.Should().Be("Seller");
        payload.CustomerUserId.Should().Be(customerUserId);
        payload.ChangedAtUtc.Should().BeOnOrAfter(before);
        payload.ChangedAtUtc.Should().BeOnOrBefore(after);
        payload.Sequence.Should().Be(1);
        message.Sequence.Should().Be(1);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldReportConcurrentUpdate_WhenTwoMutationsRace()
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
            Total = 42.5m,
            StatusVersion = 1
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        DbContextOptions<ApplicationDbContext> Options() =>
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options;

        using var dbA = new ApplicationDbContext(Options());
        using var dbB = new ApplicationDbContext(Options());
        var serviceA = CreateService(dbA);
        var serviceB = CreateService(dbB);

        // Both contexts snapshot the same order (StatusVersion = 1) before either mutates it, so
        // the second save races on the concurrency token.
        await dbA.Orders.SingleAsync(x => x.Id == order.Id);
        await dbB.Orders.SingleAsync(x => x.Id == order.Id);

        var first = await serviceA.UpdateStatusAsync(
            sellerUserId, order.Id, new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Preparing }, null);

        var second = await serviceB.UpdateStatusAsync(
            sellerUserId, order.Id, new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Cancelled }, null);

        first.ConcurrentUpdate.Should().BeFalse();
        first.Order.Should().NotBeNull();

        second.ConcurrentUpdate.Should().BeTrue();
        second.Order.Should().BeNull();

        // Only the winning mutation advanced the monotonic sequence. The losing write was rejected
        // on the StatusVersion concurrency token, so it can never emit the same sequence as the
        // winner. (The outbox/history inserts from the losing request are an InMemory-provider
        // artifact: it does not roll back already-applied inserts on a concurrency failure the way
        // PostgreSQL's transaction does; transactional atomicity is covered by the commit/rollback
        // tests in OrderOutboxIntegrationTests.)
        using var verify = new ApplicationDbContext(Options());
        var persisted = await verify.Orders.SingleAsync();
        persisted.Status.Should().Be(OrderStatus.Preparing);
        persisted.StatusVersion.Should().Be(2);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldNotEnqueueOrderStatusChangedEvent_OnInvalidTransition()
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

        var sut = new OrderService(_db, new EfUnitOfWork(_db), Mock.Of<INotificationService>(), new OutboxWriter(_db));

        var result = await sut.UpdateStatusAsync(
            sellerUserId,
            order.Id,
            new UpdateOrderStatusRequestDto { NewStatus = OrderStatus.Delivered },
            null);

        result.InvalidTransition.Should().BeTrue();
        result.Order.Should().BeNull();
        _db.OutboxMessages.Should().BeEmpty();
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

    [Theory]
    [InlineData(OrderStatus.Received)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OnDelivery)]
    public async Task CompleteForSellerBoardAsync_ShouldNotComplete_WhenStatusIsNotDelivered(OrderStatus status)
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
            Status = status,
            Total = 42.5m
        };
        _db.Stores.Add(store);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _sut.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1");

        result.InvalidState.Should().BeTrue();
        result.Order.Should().BeNull();
        (await _db.Orders.SingleAsync()).SellerCompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CompleteForSellerBoardAsync_ShouldNotComplete_Delivery_WithoutDeliveryConfirmedAtUtc()
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

        var result = await _sut.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1");

        result.InvalidState.Should().BeTrue();
        result.Order.Should().BeNull();
        (await _db.Orders.SingleAsync()).SellerCompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CompleteForSellerBoardAsync_ShouldComplete_PickUpDelivered_WithoutDeliveryConfirmedAtUtc()
    {
        var sellerUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
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

        var result = await _sut.CompleteForSellerBoardAsync(sellerUserId, order.Id, "127.0.0.1");

        result.InvalidState.Should().BeFalse();
        result.Order.Should().NotBeNull();
        result.Order!.SellerCompletedAtUtc.Should().NotBeNull();
        (await _db.Orders.SingleAsync()).SellerCompletedAtUtc.Should().NotBeNull();
    }
}
