using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Hangfire;
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
        IOutboxWorkerLiveness? liveness = null,
        HangfireRuntime? runtime = null,
        OutboxHealthCountsCache? countsCache = null,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        // The fixture context is shared by every probe in a test, so it must not be disposed when
        // the health check's per-probe scope ends.
        services.AddSingleton(_ => db);
        var provider = services.BuildServiceProvider();

        return new OutboxHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            liveness ?? new OutboxWorkerLiveness(),
            Options.Create(options),
            runtime ?? HangfireRuntime.Create(HangfireRole.All),
            countsCache ?? new OutboxHealthCountsCache(TimeSpan.FromSeconds(5), TimeProvider.System),
            timeProvider ?? TimeProvider.System);
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
    public async Task CheckHealthAsync_ShouldNotRequireLocalHeartbeat_WhenOutboxRunsInAnotherProcess()
    {
        using var db = CreateDb();

        var check = CreateCheck(
            db,
            WorkerOptions(),
            liveness: new OutboxWorkerLiveness(),
            runtime: HangfireRuntime.Create(HangfireRole.Web));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["workerRunsLocally"].Should().Be(false);
        result.Data["hangfireRole"].Should().Be("Web");
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

    [Fact]
    public async Task CheckHealthAsync_ShouldReportDeliveryBacklogAndFailuresPerChannel()
    {
        using var db = CreateDb();
        var message = new OutboxMessage
        {
            Type = "OrderCreated",
            AggregateId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            AvailableAtUtc = DateTime.UtcNow,
            Status = OutboxMessageStatus.Pending
        };
        db.OutboxMessages.Add(message);

        db.OutboxHandlerDeliveries.AddRange(
            new OutboxHandlerDelivery
            {
                OutboxMessageId = message.Id,
                HandlerKey = "business.notification",
                Channel = OutboxChannel.Business,
                Status = OutboxHandlerDeliveryStatus.Pending,
                AvailableAtUtc = DateTime.UtcNow
            },
            new OutboxHandlerDelivery
            {
                OutboxMessageId = message.Id,
                HandlerKey = "business.print",
                Channel = OutboxChannel.Business,
                Status = OutboxHandlerDeliveryStatus.Failed,
                AvailableAtUtc = DateTime.UtcNow,
                LastError = "printer offline"
            },
            new OutboxHandlerDelivery
            {
                OutboxMessageId = message.Id,
                HandlerKey = "communications.email",
                Channel = OutboxChannel.Communications,
                Status = OutboxHandlerDeliveryStatus.Pending,
                AvailableAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var liveness = new OutboxWorkerLiveness();
        liveness.RecordHeartbeat(DateTimeOffset.UtcNow);

        var check = CreateCheck(db, WorkerOptions(), liveness);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["deliveriesPending"].Should().Be(2);
        result.Data["deliveriesFailed"].Should().Be(1);
        result.Data["businessPending"].Should().Be(1);
        result.Data["businessFailed"].Should().Be(1);
        result.Data["communicationsPending"].Should().Be(1);
        result.Data["communicationsFailed"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeDegraded_WhenOnlyADeliveryFailed()
    {
        using var db = CreateDb();
        var message = new OutboxMessage
        {
            Type = "OrderCreated",
            AggregateId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            AvailableAtUtc = DateTime.UtcNow,
            // Projection has not converged to Failed yet; deliveries are authoritative.
            Status = OutboxMessageStatus.Pending
        };
        db.OutboxMessages.Add(message);
        db.OutboxHandlerDeliveries.Add(new OutboxHandlerDelivery
        {
            OutboxMessageId = message.Id,
            HandlerKey = "communications.email",
            Channel = OutboxChannel.Communications,
            Status = OutboxHandlerDeliveryStatus.Failed,
            AvailableAtUtc = DateTime.UtcNow,
            LastError = "invalid recipient"
        });
        await db.SaveChangesAsync();

        var liveness = new OutboxWorkerLiveness();
        liveness.RecordHeartbeat(DateTimeOffset.UtcNow);

        var check = CreateCheck(db, WorkerOptions(), liveness);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["deliveriesFailed"].Should().Be(1);
        result.Data["communicationsFailed"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReuseDatabaseCounts_WithinCacheWindow_AndRefreshAfterIt()
    {
        using var db = CreateDb();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var countsCache = new OutboxHealthCountsCache(TimeSpan.FromSeconds(5), clock);

        var liveness = new OutboxWorkerLiveness();
        liveness.RecordHeartbeat(clock.GetUtcNow());

        var check = CreateCheck(db, WorkerOptions(), liveness, countsCache: countsCache, timeProvider: clock);

        var first = await check.CheckHealthAsync(new HealthCheckContext());
        first.Status.Should().Be(HealthStatus.Healthy);

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

        // Still inside the cache window: the snapshot is reused and the new row is not counted yet.
        var cached = await check.CheckHealthAsync(new HealthCheckContext());
        cached.Status.Should().Be(HealthStatus.Healthy);

        clock.UtcNow = clock.UtcNow.AddSeconds(6);

        var refreshed = await check.CheckHealthAsync(new HealthCheckContext());
        refreshed.Status.Should().Be(HealthStatus.Degraded);
        refreshed.Data["failed"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldStillReportStaleWorker_WhenCountsComeFromCache()
    {
        using var db = CreateDb();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var countsCache = new OutboxHealthCountsCache(TimeSpan.FromSeconds(30), clock);

        var liveness = new OutboxWorkerLiveness();
        liveness.RecordHeartbeat(clock.GetUtcNow());

        var check = CreateCheck(db, WorkerOptions(staleAfter: TimeSpan.FromMinutes(2)), liveness, countsCache: countsCache, timeProvider: clock);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);

        // Heartbeats are never cached, so staleness is reported even though the counts are reused.
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
