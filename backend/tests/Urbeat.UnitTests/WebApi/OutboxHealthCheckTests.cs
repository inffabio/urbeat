using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Urbeat.WebApi.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Urbeat.UnitTests.WebApi;

public sealed class OutboxHealthCheckTests
{
    private static OutboxHealthCheck CreateCheck(
        ApplicationDbContext db,
        OutboxOptions options,
        IOutboxWorkerLiveness? liveness = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped(_ => db);
        var provider = services.BuildServiceProvider();

        return new OutboxHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            liveness ?? new OutboxWorkerLiveness(),
            Options.Create(options));
    }

    private static ApplicationDbContext CreateDb()
    {
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    private static OutboxOptions WorkerOptions(int backlogThreshold = 1000, TimeSpan? staleAfter = null) => new()
    {
        WorkerEnabled = true,
        BacklogThreshold = backlogThreshold,
        WorkerStaleAfter = staleAfter ?? TimeSpan.FromMinutes(2)
    };

    [Fact]
    public async Task CheckHealthAsync_ShouldBeHealthy_WhenWorkerHeartbeatIsFresh_AndNoBacklogOrFailures()
    {
        using var db = CreateDb();
        var liveness = new OutboxWorkerLiveness();
        liveness.RecordHeartbeat(DateTimeOffset.UtcNow);

        var check = CreateCheck(db, WorkerOptions(), liveness);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["pending"].Should().Be(0);
        result.Data["failed"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeUnhealthy_WhenWorkerHasNeverHeartbeated()
    {
        using var db = CreateDb();

        var check = CreateCheck(db, WorkerOptions(), new OutboxWorkerLiveness());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeUnhealthy_WhenWorkerHeartbeatIsStale()
    {
        using var db = CreateDb();
        var liveness = new OutboxWorkerLiveness();
        liveness.RecordHeartbeat(DateTimeOffset.UtcNow.AddMinutes(-5));

        var check = CreateCheck(db, WorkerOptions(staleAfter: TimeSpan.FromMinutes(2)), liveness);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldNotFlagStaleWorker_WhenWorkerIsDisabled()
    {
        using var db = CreateDb();

        var check = CreateCheck(db, new OutboxOptions { WorkerEnabled = false, BacklogThreshold = 1000 });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeDegraded_WhenFailedMessagesExist()
    {
        using var db = CreateDb();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = "OrderCreated",
            AggregateId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            AvailableAtUtc = DateTime.UtcNow,
            Status = OutboxMessageStatus.Failed,
            LastError = "boom"
        });
        await db.SaveChangesAsync();

        var liveness = new OutboxWorkerLiveness();
        liveness.RecordHeartbeat(DateTimeOffset.UtcNow);

        var check = CreateCheck(db, WorkerOptions(), liveness);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["failed"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeDegraded_WhenBacklogExceedsThreshold()
    {
        using var db = CreateDb();
        for (var i = 0; i < 5; i++)
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Type = "OrderCreated",
                AggregateId = Guid.NewGuid(),
                OccurredAtUtc = DateTime.UtcNow,
                AvailableAtUtc = DateTime.UtcNow,
                Status = OutboxMessageStatus.Pending
            });
        }
        await db.SaveChangesAsync();

        var liveness = new OutboxWorkerLiveness();
        liveness.RecordHeartbeat(DateTimeOffset.UtcNow);

        var check = CreateCheck(db, WorkerOptions(backlogThreshold: 3), liveness);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["pending"].Should().Be(5);
    }
}
