using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Urbeat.Infrastructure.Security;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class SignalRAccessTokenResolverTests
{
    [Theory]
    [InlineData("/hubs/seller-notifications")]
    [InlineData("/hubs/customer-notifications")]
    public void Resolve_ShouldReturnQueryStringToken_ForHubRequests(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?access_token=bearer-token");

        SignalRAccessTokenResolver.Resolve(context.Request).Should().Be("bearer-token");
    }

    [Fact]
    public void Resolve_ShouldReturnNull_ForNonHubRequests()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/orders";
        context.Request.QueryString = new QueryString("?access_token=bearer-token");

        SignalRAccessTokenResolver.Resolve(context.Request).Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenTokenIsMissingOrBlank()
    {
        var withoutToken = new DefaultHttpContext();
        withoutToken.Request.Path = "/hubs/seller-notifications";

        var blankToken = new DefaultHttpContext();
        blankToken.Request.Path = "/hubs/seller-notifications";
        blankToken.Request.QueryString = new QueryString("?access_token=");

        SignalRAccessTokenResolver.Resolve(withoutToken.Request).Should().BeNull();
        SignalRAccessTokenResolver.Resolve(blankToken.Request).Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldReturnNull_ForPathsThatOnlyShareTheHubPrefix()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/hubsomething";
        context.Request.QueryString = new QueryString("?access_token=bearer-token");

        SignalRAccessTokenResolver.Resolve(context.Request).Should().BeNull();
    }
}
