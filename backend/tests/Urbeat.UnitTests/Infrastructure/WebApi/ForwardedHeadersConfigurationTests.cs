using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbeat.WebApi.Infrastructure;

namespace Urbeat.UnitTests.Infrastructure.WebApi;

public sealed class ForwardedHeadersConfigurationTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value))
            .Build();

    [Fact]
    public void Create_ShouldPreserveForwardedProtoAndFor()
    {
        var options = ForwardedHeadersConfiguration.Create(new ConfigurationBuilder().Build());

        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedFor);
        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedProto);
    }

    [Fact]
    public void Create_ShouldNotTrustArbitraryProxy_WhenNothingIsConfigured()
    {
        var options = ForwardedHeadersConfiguration.Create(new ConfigurationBuilder().Build());

        options.KnownProxies.Should().NotContain(IPAddress.Parse("203.0.113.10"));
        options.KnownNetworks.Should().NotContain(network => network.Contains(IPAddress.Parse("203.0.113.10")));
    }

    [Fact]
    public async Task SpoofedForwardedFor_ShouldNotChangeRemoteIp_WhenNoTrustedProxyIsConfigured()
    {
        var options = ForwardedHeadersConfiguration.Create(new ConfigurationBuilder().Build());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));

        await middleware.Invoke(context);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("198.51.100.7"));
    }

    [Fact]
    public void Create_ShouldRegisterConfiguredProxy()
    {
        var configuration = BuildConfiguration(("ForwardedHeaders:KnownProxies:0", "198.51.100.7"));

        var options = ForwardedHeadersConfiguration.Create(configuration);

        options.KnownProxies.Should().Contain(IPAddress.Parse("198.51.100.7"));
    }

    [Fact]
    public async Task ForwardedFor_ShouldChangeRemoteIp_WhenProxyIsTrusted()
    {
        var configuration = BuildConfiguration(("ForwardedHeaders:KnownProxies:0", "198.51.100.7"));
        var options = ForwardedHeadersConfiguration.Create(configuration);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));

        await middleware.Invoke(context);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.10"));
    }

    [Fact]
    public void Create_ShouldRegisterConfiguredNetwork()
    {
        var configuration = BuildConfiguration(("ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12"));

        var options = ForwardedHeadersConfiguration.Create(configuration);

        options.KnownNetworks.Should().Contain(network => network.Contains(IPAddress.Parse("172.20.0.5")));
    }

    [Fact]
    public void Create_ShouldIgnoreInvalidEntries()
    {
        var configuration = BuildConfiguration(
            ("ForwardedHeaders:KnownProxies:0", "not-an-ip"),
            ("ForwardedHeaders:KnownNetworks:0", "not-a-network"));

        var options = ForwardedHeadersConfiguration.Create(configuration);

        options.KnownProxies.Should().NotContain(IPAddress.Parse("203.0.113.10"));
        options.KnownNetworks.Should().NotContain(network => network.Contains(IPAddress.Parse("203.0.113.10")));
    }
}
