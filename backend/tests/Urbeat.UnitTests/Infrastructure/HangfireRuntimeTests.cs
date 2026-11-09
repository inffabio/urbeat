using System.Reflection;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Urbeat.Infrastructure.Data;
using Urbeat.Infrastructure.Hangfire;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class HangfireRuntimeTests
{
    [Fact]
    public void Resolve_ShouldDefaultToLegacyAllRole_WhenRoleIsNotConfigured()
    {
        var runtime = HangfireRuntime.Resolve(Configuration());

        runtime.Role.Should().Be(HangfireRole.All);
        runtime.RunsServer.Should().BeTrue();
        runtime.RunsOutbox.Should().BeTrue();
        runtime.OutboxChannels.Should().Equal(OutboxChannel.Business, OutboxChannel.Communications);
        runtime.RegistersRecurringJobs.Should().BeTrue();
    }

    [Theory]
    [InlineData("Web", HangfireRole.Web)]
    [InlineData("business", HangfireRole.Business)]
    [InlineData("COMMUNICATIONS", HangfireRole.Communications)]
    public void Resolve_ShouldParseRoleCaseInsensitively(string value, HangfireRole expected)
    {
        var runtime = HangfireRuntime.Resolve(Configuration((HangfireRuntime.RoleKey, value)));

        runtime.Role.Should().Be(expected);
    }

    [Fact]
    public void Resolve_ShouldRejectUnknownRole()
    {
        var act = () => HangfireRuntime.Resolve(Configuration((HangfireRuntime.RoleKey, "checkout")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Hangfire:Role*");
    }

    [Fact]
    public void Resolve_ShouldRejectInvalidRunServerValue()
    {
        var act = () => HangfireRuntime.Resolve(Configuration((HangfireRuntime.RunServerKey, "maybe")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Hangfire:RunServer*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Resolve_ShouldRejectInvalidWorkerCount(string value)
    {
        var act = () => HangfireRuntime.Resolve(Configuration((HangfireRuntime.WorkerCountKey, value)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Hangfire:WorkerCount*");
    }

    [Fact]
    public void WebRole_ShouldNotRunServerOrSchedulerByDefault()
    {
        var runtime = HangfireRuntime.Create(HangfireRole.Web);

        runtime.RunsServer.Should().BeFalse();
        runtime.RunsOutbox.Should().BeFalse();
        runtime.OutboxChannels.Should().BeEmpty();
        runtime.RegistersRecurringJobs.Should().BeFalse();
        runtime.Queues.Should().Equal(HangfireQueues.Default);
    }

    [Fact]
    public void RunServerOverride_ShouldAllowWebRoleToProcessOnlyDefaultQueue()
    {
        var runtime = HangfireRuntime.Create(HangfireRole.Web, runsServer: true);

        runtime.RunsServer.Should().BeTrue();
        runtime.RunsOutbox.Should().BeFalse();
        runtime.RegistersRecurringJobs.Should().BeFalse();
        runtime.Queues.Should().Equal(HangfireQueues.Default);
    }

    [Fact]
    public void CommunicationsWorker_ShouldConsumeOnlyCommunicationsQueue()
    {
        var runtime = HangfireRuntime.Create(HangfireRole.Communications);

        runtime.Queues.Should().Equal(HangfireQueues.Communications);
        runtime.Queues.Should().NotContain(HangfireQueues.Business);
        runtime.Queues.Should().NotContain(HangfireQueues.Default);
        runtime.RunsOutbox.Should().BeTrue();
        runtime.OutboxChannels.Should().Equal(OutboxChannel.Communications);
        runtime.RegistersRecurringJobs.Should().BeFalse();
    }

    [Fact]
    public void BusinessWorker_ShouldConsumeBusinessAndDefaultButNotCommunications()
    {
        var runtime = HangfireRuntime.Create(HangfireRole.Business);

        runtime.Queues.Should().Equal(HangfireQueues.Business, HangfireQueues.Default);
        runtime.Queues.Should().NotContain(HangfireQueues.Communications);
        runtime.RunsOutbox.Should().BeTrue();
        runtime.OutboxChannels.Should().Equal(OutboxChannel.Business);
        runtime.RegistersRecurringJobs.Should().BeTrue();
    }

    [Fact]
    public void CommunicationJobs_ShouldDeclareCommunicationsQueue()
    {
        Type[] communicationJobs =
        [
            typeof(SendEmailConfirmationJob),
            typeof(SendPasswordResetEmailJob)
        ];

        communicationJobs
            .Select(QueueNameOf)
            .Should()
            .OnlyContain(queue => queue == HangfireQueues.Communications);
    }

    [Fact]
    public void BusinessJobs_ShouldDeclareBusinessQueue()
    {
        Type[] businessJobs =
        [
            typeof(OperationalHeartbeatJob),
            typeof(SellerSubscriptionNotificationJob),
            typeof(ImportAllCitiesNeighborhoodsJob),
            typeof(ImportStoreNeighborhoodsJob),
            typeof(ImportUfNeighborhoodsJob),
            typeof(GooglePlacesTextSearchImporter)
        ];

        businessJobs
            .Select(QueueNameOf)
            .Should()
            .OnlyContain(queue => queue == HangfireQueues.Business);
    }

    [Fact]
    public void AddUrbeatHangfireServer_ShouldNotRegisterHangfireServerForWebRole()
    {
        var services = new ServiceCollection();

        services.AddUrbeatHangfireServer(HangfireRuntime.Create(HangfireRole.Web));

        HasHangfireServer(services).Should().BeFalse();
    }

    [Theory]
    [InlineData(HangfireRole.Business)]
    [InlineData(HangfireRole.Communications)]
    [InlineData(HangfireRole.All)]
    public void AddUrbeatHangfireServer_ShouldRegisterHangfireServerForQueueConsumers(HangfireRole role)
    {
        var services = new ServiceCollection();

        services.AddUrbeatHangfireServer(HangfireRuntime.Create(role));

        HasHangfireServer(services).Should().BeTrue();
    }

    [Theory]
    [InlineData(HangfireRole.Web)]
    [InlineData(HangfireRole.Communications)]
    public void RecurringJobs_ShouldNotBeRegisteredOutsideTheSchedulerRole(HangfireRole role)
    {
        var manager = new RecordingRecurringJobManager();

        var registered = HangfireRecurringJobs.TryRegisterBusinessJobs(manager, role);

        registered.Should().BeFalse();
        manager.Registrations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HangfireRole.Business)]
    [InlineData(HangfireRole.All)]
    public void RecurringJobs_ShouldBeRegisteredOnlyByTheSchedulerRole(HangfireRole role)
    {
        var manager = new RecordingRecurringJobManager();

        var registered = HangfireRecurringJobs.TryRegisterBusinessJobs(manager, role);

        registered.Should().BeTrue();
        manager.Registrations.Select(registration => registration.Id).Should().BeEquivalentTo(
            HangfireRecurringJobs.OperationalHeartbeatJobId,
            HangfireRecurringJobs.SellerSubscriptionNotificationJobId);
#pragma warning disable CS0618 // Hangfire 1.8 stores the recurring queue on RecurringJobOptions.
        manager.Registrations
            .All(registration => registration.Options is not null
                && registration.Options.QueueName == HangfireQueues.Business)
            .Should()
            .BeTrue();
#pragma warning restore CS0618
    }

    private static string? QueueNameOf(Type jobType) =>
        jobType.GetCustomAttribute<QueueAttribute>()?.Queue;

    private static bool HasHangfireServer(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService));

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();

    private sealed class RecordingRecurringJobManager : IRecurringJobManager
    {
        public List<RecurringJobRegistration> Registrations { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            Registrations.Add(new RecurringJobRegistration(recurringJobId, job, cronExpression, options));
        }

        public void Trigger(string recurringJobId)
        {
        }

        public void RemoveIfExists(string recurringJobId)
        {
        }
    }

    private sealed record RecurringJobRegistration(
        string Id,
        Job Job,
        string CronExpression,
        RecurringJobOptions Options);
}
