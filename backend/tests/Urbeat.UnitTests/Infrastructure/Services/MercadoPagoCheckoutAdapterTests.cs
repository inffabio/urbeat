using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class MercadoPagoCheckoutAdapterTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public MercadoPagoCheckoutAdapterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-mp-adapter-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void MercadoPagoOptions_ShouldDefaultAllowSimulationToFalse()
    {
        new MercadoPagoOptions().AllowSimulation.Should().BeFalse();
    }

    [Fact]
    public async Task GetPaymentDetailsAsync_ShouldThrow_WhenAccessTokenMissingAndSimulationDisabled()
    {
        var sut = CreateAdapter(allowSimulation: false);

        var act = () => sut.GetPaymentDetailsAsync("txn-real", null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetPaymentDetailsAsync_ShouldReturnSimulatedApproved_WhenAccessTokenMissingAndSimulationEnabled()
    {
        var sut = CreateAdapter(allowSimulation: true);

        var details = await sut.GetPaymentDetailsAsync("txn-simulated", null);

        details.TransactionId.Should().Be("txn-simulated");
        details.Status.Should().Be("approved");
        details.IsSimulated.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCheckoutAsync_ShouldThrow_WhenAccessTokenMissingAndSimulationDisabled()
    {
        var sut = CreateAdapter(allowSimulation: false);

        var act = () => sut.CreateCheckoutAsync(CreateRequest(), null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateCheckoutAsync_ShouldReturnFakeCheckout_WhenAccessTokenMissingAndSimulationEnabled()
    {
        var sut = CreateAdapter(allowSimulation: true);

        var result = await sut.CreateCheckoutAsync(CreateRequest(), null);

        result.TransactionId.Should().StartWith("pref_");
        result.CheckoutUrl.Should().NotBeNullOrWhiteSpace();
    }

    private static MercadoPagoCheckoutCreateRequest CreateRequest()
    {
        return new MercadoPagoCheckoutCreateRequest
        {
            ExternalReference = "order-1",
            PayerEmail = "cliente@teste.com",
            Items =
            [
                new MercadoPagoCheckoutItem
                {
                    Title = "Pizza",
                    Quantity = 1,
                    UnitPrice = 10m
                }
            ]
        };
    }

    private MercadoPagoCheckoutAdapter CreateAdapter(bool allowSimulation)
    {
        var options = Options.Create(new MercadoPagoOptions
        {
            AccessToken = string.Empty,
            AllowSimulation = allowSimulation
        });

        return new MercadoPagoCheckoutAdapter(
            new HttpClient(),
            options,
            Mock.Of<IEncryptionService>(),
            _db);
    }
}
