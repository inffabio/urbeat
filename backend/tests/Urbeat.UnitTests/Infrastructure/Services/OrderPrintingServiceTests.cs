using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class OrderPrintingServiceTests
{
    private static ApplicationDbContext CreateDbContext(string name) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static OrderPrintJob Job(Guid storeId, Guid sellerUserId, string code) => new()
    {
        OrderId = Guid.NewGuid(),
        StoreId = storeId,
        SellerUserId = sellerUserId,
        Code = code,
        AcceptanceSequence = 2,
        AcceptedAtUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
        Status = OrderPrintJobStatus.Pending
    };

    [Fact]
    public async Task ListPendingAsync_returns_only_pending_jobs_from_the_seller_store_and_paginates_by_cursor()
    {
        var dbName = $"urbeat-print-list-{Guid.NewGuid()}";
        await using var db = CreateDbContext(dbName);
        var sellerUserId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja", Slug = $"loja-{Guid.NewGuid():N}", PhoneNumber = "11999999999" };
        var otherStore = new Store { OwnerUserId = otherSellerId, Name = "Outra", Slug = $"outra-{Guid.NewGuid():N}", PhoneNumber = "11999999998" };
        db.Stores.AddRange(store, otherStore);

        for (var i = 0; i < 3; i += 1)
        {
            var order = new Order { Code = $"URB-{i}", CustomerUserId = Guid.NewGuid(), StoreId = store.Id, Status = OrderStatus.Preparing };
            db.Orders.Add(order);
            db.OrderPrintJobs.Add(new OrderPrintJob
            {
                OrderId = order.Id,
                StoreId = store.Id,
                SellerUserId = sellerUserId,
                Code = order.Code,
                AcceptanceSequence = 2,
                AcceptedAtUtc = DateTime.UtcNow
            });
        }

        var otherOrder = new Order { Code = "OTH-1", CustomerUserId = Guid.NewGuid(), StoreId = otherStore.Id, Status = OrderStatus.Preparing };
        db.Orders.Add(otherOrder);
        db.OrderPrintJobs.Add(new OrderPrintJob
        {
            OrderId = otherOrder.Id,
            StoreId = otherStore.Id,
            SellerUserId = otherSellerId,
            Code = otherOrder.Code,
            AcceptanceSequence = 2,
            AcceptedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new OrderPrintingService(db);

        var first = await service.ListPendingAsync(sellerUserId, 0, 2);
        first.Items.Should().HaveCount(2);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().Be(first.Items.Last().Cursor);

        var second = await service.ListPendingAsync(sellerUserId, first.NextCursor, 2);
        second.Items.Should().ContainSingle();
        second.HasMore.Should().BeFalse();
        second.Items.Should().OnlyContain(job => job.StoreId == store.Id);
        second.Items.Should().NotContain(job => job.OrderCode == "OTH-1");
    }

    [Fact]
    public async Task RecordResultAsync_completes_on_success_and_forbids_another_seller()
    {
        await using var db = CreateDbContext($"urbeat-print-result-{Guid.NewGuid()}");
        var sellerUserId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja", Slug = $"loja-{Guid.NewGuid():N}", PhoneNumber = "11999999999" };
        var order = new Order
        {
            Code = "URB-ACK",
            CustomerUserId = Guid.NewGuid(),
            StoreId = store.Id,
            Status = OrderStatus.Preparing
        };
        var job = Job(store.Id, sellerUserId, order.Code);
        job.OrderId = order.Id;
        db.Stores.Add(store);
        db.Orders.Add(order);
        db.OrderPrintJobs.Add(job);
        await db.SaveChangesAsync();

        var service = new OrderPrintingService(db);
        var forbidden = await service.RecordResultAsync(otherSellerId, order.Id, new RecordPrintJobResultRequestDto
        {
            Success = true,
            OperationKey = "key-1"
        });
        forbidden.NotFound.Should().BeTrue();

        var success = await service.RecordResultAsync(sellerUserId, order.Id, new RecordPrintJobResultRequestDto
        {
            Success = true,
            OperationKey = "key-1",
            Message = "enviado"
        });

        success.Job.Should().NotBeNull();
        (await db.OrderPrintJobs.SingleAsync()).Status.Should().Be(OrderPrintJobStatus.Completed);
        (await db.OrderPrintJobs.SingleAsync()).AttemptCount.Should().Be(1);
        (await service.ListPendingAsync(sellerUserId, 0, 10)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordResultAsync_marks_ambiguous_and_stops_automatic_recovery()
    {
        await using var db = CreateDbContext($"urbeat-print-ambiguous-{Guid.NewGuid()}");
        var sellerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja", Slug = $"loja-{Guid.NewGuid():N}", PhoneNumber = "11999999999" };
        var order = new Order { Code = "URB-AMB", CustomerUserId = Guid.NewGuid(), StoreId = store.Id, Status = OrderStatus.Preparing };
        var job = Job(store.Id, sellerUserId, order.Code);
        job.OrderId = order.Id;
        db.Stores.Add(store);
        db.Orders.Add(order);
        db.OrderPrintJobs.Add(job);
        await db.SaveChangesAsync();

        var service = new OrderPrintingService(db);
        await service.RecordResultAsync(sellerUserId, order.Id, new RecordPrintJobResultRequestDto
        {
            Success = false,
            Ambiguous = true,
            OperationKey = "key-amb",
            Message = "Agente caiu durante o envio."
        });

        var recovered = await service.ListPendingAsync(sellerUserId, 0, 10);
        recovered.Items.Should().ContainSingle();
        recovered.Items.Single().Status.Should().Be("ambiguous");
        (await db.OrderPrintJobs.SingleAsync()).Status.Should().Be(OrderPrintJobStatus.Ambiguous);
    }

    [Fact]
    public async Task RecordResultAsync_keeps_failed_jobs_pending_for_recovery()
    {
        await using var db = CreateDbContext($"urbeat-print-fail-{Guid.NewGuid()}");
        var sellerUserId = Guid.NewGuid();
        var store = new Store { OwnerUserId = sellerUserId, Name = "Loja", Slug = $"loja-{Guid.NewGuid():N}", PhoneNumber = "11999999999" };
        var order = new Order { Code = "URB-FAIL", CustomerUserId = Guid.NewGuid(), StoreId = store.Id, Status = OrderStatus.Preparing };
        var job = Job(store.Id, sellerUserId, order.Code);
        job.OrderId = order.Id;
        db.Stores.Add(store);
        db.Orders.Add(order);
        db.OrderPrintJobs.Add(job);
        await db.SaveChangesAsync();

        var service = new OrderPrintingService(db);
        await service.RecordResultAsync(sellerUserId, order.Id, new RecordPrintJobResultRequestDto
        {
            Success = false,
            OperationKey = "key-1",
            Message = "Impressora sem papel"
        });

        var pending = await service.ListPendingAsync(sellerUserId, 0, 10);
        pending.Items.Should().ContainSingle();
        pending.Items.Single().AttemptCount.Should().Be(1);
        pending.Items.Single().LastError.Should().Be("Impressora sem papel");
    }
}
