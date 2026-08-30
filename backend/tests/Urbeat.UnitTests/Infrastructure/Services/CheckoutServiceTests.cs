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
}
