using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using System.Reflection;
using System.Security.Claims;
using Urbeat.WebApi.Hubs;

namespace Urbeat.UnitTests.WebApi;

/// <summary>
/// Documents the security boundary of the anonymous customer hub: it is only anonymous because
/// <c>DeliveryAreaUpdated</c> is broadcast to a public store group. The hub itself exposes no
/// order data — the only methods are group join/leave — while order events are sent exclusively
/// through <c>IHubContext.Clients.User(customerUserId)</c> (see NotificationServiceTests).
/// </summary>
public sealed class CustomerNotificationHubTests
{
    [Fact]
    public void Hub_ShouldBeAnonymousOnlyForPublicStoreGroup_AndExposeNoOrderData()
    {
        typeof(CustomerNotificationHub).GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();

        var publicMethods = typeof(CustomerNotificationHub)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(x => x.Name)
            .OrderBy(x => x)
            .ToArray();

        publicMethods.Should().Equal("JoinStore", "LeaveStore");
    }

    [Fact]
    public async Task JoinStore_ShouldOnlyAddConnectionToStoreGroup()
    {
        var groups = new RecordingGroupManager();
        var hub = new CustomerNotificationHub
        {
            Context = new FakeHubCallerContext(),
            Groups = groups
        };

        await hub.JoinStore("abc-123");

        groups.Added.Should().ContainSingle(x => x.ConnectionId == "conn-123" && x.GroupName == "store-abc-123");
        groups.Removed.Should().BeEmpty();
    }

    [Fact]
    public async Task LeaveStore_ShouldOnlyRemoveConnectionFromStoreGroup()
    {
        var groups = new RecordingGroupManager();
        var hub = new CustomerNotificationHub
        {
            Context = new FakeHubCallerContext(),
            Groups = groups
        };

        await hub.LeaveStore("abc-123");

        groups.Removed.Should().ContainSingle(x => x.ConnectionId == "conn-123" && x.GroupName == "store-abc-123");
        groups.Added.Should().BeEmpty();
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = new();
        public List<(string ConnectionId, string GroupName)> Removed { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        public override string ConnectionId => "conn-123";

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => default;

        public override void Abort()
        {
        }
    }
}
