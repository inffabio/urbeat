using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// Relational assertions for <see cref="PaymentService"/> that require a real PostgreSQL instance.
///
/// The InMemory provider used by the rest of the suite cannot reproduce the PostgreSQL
/// <c>UniqueViolation</c> rollback path: it has no transactions, no advisory locks, and no
/// provider-specific unique-violation SQL state. These tests therefore run against a Testcontainers
/// PostgreSQL container and are skipped when Docker is unavailable.
/// </summary>
public sealed class PaymentServiceRelationalTests
{
    private readonly ITestOutputHelper _output;

    public PaymentServiceRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task UniqueViolation_ShouldRollbackAbortedTransaction_AndReturnWinningPayment()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;

            await using var setupContext = new ApplicationDbContext(options);
            await setupContext.Database.EnsureCreatedAsync();
            var order = await SeedOrderAsync(setupContext);

            // Two independent connections to the same database: the winner and the system under test.
            await using var winnerContext = new ApplicationDbContext(options);
            await using var sutContext = new ApplicationDbContext(options);

            var gatewayReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseGateway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var adapterMock = new Mock<IMercadoPagoCheckoutAdapter>();
            adapterMock
                .Setup(x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(async (MercadoPagoCheckoutCreateRequest _, Guid? _, CancellationToken _) =>
                {
                    gatewayReached.TrySetResult();
                    await releaseGateway.Task;
                    return new MercadoPagoCheckoutCreateResponse
                    {
                        TransactionId = "pref_winner",
                        CheckoutUrl = "https://checkout/winner",
                        RawPayload = "{}"
                    };
                });

            var strategy = new MercadoPagoOrderPaymentStrategy(sutContext, adapterMock.Object);
            var factory = new OrderPaymentStrategyFactory([strategy]);
            var sut = new PaymentService(sutContext, new EfUnitOfWork(sutContext), factory);

            var request = new CreateOrderPaymentRequestDto { OrderId = order.Id };

            var sutTask = sut.CreateOrderPaymentAsync(order.CustomerUserId, request, null, CancellationToken.None);

            // Wait until the SUT has read its (empty) payment view and reached the gateway.
            await gatewayReached.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // The winning request persists the payment on a separate connection while the SUT is
            // still mid-flight. This forces the SUT's INSERT to hit the unique index.
            winnerContext.Payments.Add(new Payment
            {
                OrderId = order.Id,
                Gateway = PaymentGateway.MercadoPago,
                GatewayTransactionId = "pref_winner",
                Method = order.PaymentMethod,
                Amount = order.Total,
                Status = PaymentStatus.Pending
            });
            await winnerContext.SaveChangesAsync();

            releaseGateway.TrySetResult();

            var result = await sutTask.WaitAsync(TimeSpan.FromSeconds(20));

            result.Payment.Should().NotBeNull();
            result.Payment!.PaymentId.Should().NotBeEmpty();
            result.Payment.GatewayTransactionId.Should().Be("pref_winner");

            adapterMock.Verify(
                x => x.CreateCheckoutAsync(It.IsAny<MercadoPagoCheckoutCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Once);

            await using var verifyContext = new ApplicationDbContext(options);
            (await verifyContext.Payments.CountAsync(x => x.OrderId == order.Id)).Should().Be(1);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }

    private static async Task<Order> SeedOrderAsync(ApplicationDbContext dbContext)
    {
        var order = new Order
        {
            Code = Guid.NewGuid().ToString("N")[..16],
            CustomerUserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = PaymentMethod.PixOnline,
            Status = OrderStatus.PendingPayment,
            Subtotal = 30m,
            DeliveryFee = 5m,
            Total = 35m
        };

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();
        return order;
    }
}
