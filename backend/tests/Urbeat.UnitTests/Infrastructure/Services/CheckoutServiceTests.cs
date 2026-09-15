using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class CheckoutServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly CheckoutService _sut;

    public CheckoutServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-checkout-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        var userManager = new UserManager<IdentityUser<Guid>>(
            Mock.Of<IUserStore<IdentityUser<Guid>>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

        _sut = new CheckoutService(
            _db,
            new EfUnitOfWork(_db),
            Mock.Of<INotificationService>(),
            Mock.Of<IOutboxWriter>(),
            userManager,
            new PricingService());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ConfirmAsync_ShouldPersistOrderCode_WithUrbPrefix()
    {
        var customerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            IsOpen = true,
            IsSubscriptionBlocked = false
        };
        var product = new Product
        {
            StoreId = store.Id,
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Price = 40m,
            SaleMode = "single",
            IsAvailable = true
        };
        _db.Stores.Add(store);
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var result = await _sut.ConfirmAsync(
            customerUserId,
            new CheckoutRequestDto
            {
                StoreId = store.Id,
                FulfillmentType = FulfillmentType.PickUp,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                Items = new[] { new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 } }
            },
            "127.0.0.1");

        result.Confirmation.Should().NotBeNull();
        result.Confirmation!.Code.Should().MatchRegex("^URB-[A-Z0-9]{8}$");
        result.Confirmation.Code.Should().NotStartWith("HAP-");
    }

    [Fact]
    public async Task PreviewAsync_ShouldApplyDailyFreeShipping_WhenEnabledToday_ForAnyDestination()
    {
        var (store, product, address) = await SeedDeliveryStoreAsync(
            freeShippingToday: true,
            freeShippingTodayDate: StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow),
            neighborhood: "Bairro Fora da Cobertura");

        var result = await PreviewDeliveryAsync(store, product, address);

        result.DeliveryAreaNotCovered.Should().BeFalse();
        result.Summary.Should().NotBeNull();
        result.Summary!.DeliveryFee.Should().Be(0m);
        result.Summary.FreeShippingApplied.Should().BeTrue();
    }

    [Fact]
    public async Task PreviewAsync_ShouldNotApplyDailyFreeShipping_WhenDateExpired()
    {
        var (store, product, address) = await SeedDeliveryStoreAsync(
            freeShippingToday: true,
            freeShippingTodayDate: StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1));

        var result = await PreviewDeliveryAsync(store, product, address);

        result.Summary.Should().NotBeNull();
        result.Summary!.DeliveryFee.Should().Be(9m);
        result.Summary.FreeShippingApplied.Should().BeFalse();
    }

    [Fact]
    public async Task PreviewAsync_ShouldNotApplyDailyFreeShipping_WhenDateMissing()
    {
        var (store, product, address) = await SeedDeliveryStoreAsync(
            freeShippingToday: true,
            freeShippingTodayDate: null);

        var result = await PreviewDeliveryAsync(store, product, address);

        result.Summary.Should().NotBeNull();
        result.Summary!.DeliveryFee.Should().Be(9m);
        result.Summary.FreeShippingApplied.Should().BeFalse();
    }

    [Fact]
    public async Task PreviewAsync_ShouldNotApplyDailyFreeShipping_ForPickUp()
    {
        var (store, product, address) = await SeedDeliveryStoreAsync(
            freeShippingToday: true,
            freeShippingTodayDate: StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow));

        var result = await _sut.PreviewAsync(
            address.UserId,
            new CheckoutRequestDto
            {
                StoreId = store.Id,
                FulfillmentType = FulfillmentType.PickUp,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                Items = new[] { new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 } }
            });

        result.Summary.Should().NotBeNull();
        result.Summary!.DeliveryFee.Should().Be(0m);
        result.Summary.FreeShippingApplied.Should().BeFalse();
    }

    private Task<CheckoutResultDto> PreviewDeliveryAsync(Store store, Product product, CustomerAddress address)
    {
        return _sut.PreviewAsync(
            address.UserId,
            new CheckoutRequestDto
            {
                StoreId = store.Id,
                FulfillmentType = FulfillmentType.Delivery,
                CustomerAddressId = address.Id,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                Items = new[] { new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 } }
            });
    }

    private async Task<(Store Store, Product Product, CustomerAddress Address)> SeedDeliveryStoreAsync(
        bool freeShippingToday,
        DateOnly? freeShippingTodayDate,
        string neighborhood = "Centro")
    {
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Frete",
            Slug = $"loja-frete-{Guid.NewGuid():N}",
            PhoneNumber = "11999999999",
            IsOpen = true,
            IsSubscriptionBlocked = false,
            SupportsDelivery = true,
            SupportsPickup = true,
            DeliveryFee = 9m,
            MinimumOrderValue = 0m,
            FreeShippingToday = freeShippingToday,
            FreeShippingTodayDate = freeShippingTodayDate
        };
        store.DeliveryAreas.Add(new StoreDeliveryArea { Neighborhood = "Centro", DeliveryFee = 9m, IsActive = true });

        var product = new Product
        {
            StoreId = store.Id,
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Price = 40m,
            SaleMode = "single",
            IsAvailable = true
        };

        var address = new CustomerAddress
        {
            UserId = Guid.NewGuid(),
            Cep = "01001000",
            Street = "Rua A",
            Number = "1",
            Neighborhood = neighborhood,
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true
        };

        _db.Stores.Add(store);
        _db.Products.Add(product);
        _db.CustomerAddresses.Add(address);
        await _db.SaveChangesAsync();

        return (store, product, address);
    }
}
