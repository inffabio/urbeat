using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Urbeat.Application.Security;
using Urbeat.Infrastructure.Security;
using Urbeat.WebApi.Security;

namespace Urbeat.UnitTests.WebApi;

/// <summary>
/// Authorization matrix of the delegated store surface: seller unchanged, admin only with a live
/// context, store mismatch rejected and customer always denied.
/// </summary>
public sealed class StoreAccessAuthorizationHandlerTests
{
    private readonly StoreAccessAuthorizationHandler _handler = new(new HttpContextAccessor());
    private readonly StoreAccessRequirement _requirement = new();

    [Fact]
    public async Task Seller_WithoutAContext_ShouldPass()
    {
        var httpContext = CreateHttpContext("Seller");

        var result = await AuthorizeAsync(httpContext);

        result.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_WithoutAContext_ShouldFail()
    {
        var httpContext = CreateHttpContext("Admin");

        var result = await AuthorizeAsync(httpContext);

        result.HasSucceeded.Should().BeFalse();
        httpContext.Items[StoreAccessAuthorizationHandler.FailureCodeItemKey]
            .Should().Be(AdminStoreErrorCodes.ContextInvalid);
    }

    [Fact]
    public async Task Admin_WithAValidContext_AndNoRouteStore_ShouldPass()
    {
        var storeId = Guid.NewGuid();
        var httpContext = CreateHttpContext("Admin", Feature(storeId));

        var result = await AuthorizeAsync(httpContext);

        result.HasSucceeded.Should().BeTrue();
        httpContext.Items.Should().NotContainKey(StoreAccessAuthorizationHandler.FailureCodeItemKey);
    }

    [Fact]
    public async Task Admin_WithAContextForAnotherStore_ShouldFailWithMismatch()
    {
        var storeId = Guid.NewGuid();
        var httpContext = CreateHttpContext("Admin", Feature(storeId));
        httpContext.Request.RouteValues["storeId"] = Guid.NewGuid().ToString();

        var result = await AuthorizeAsync(httpContext);

        result.HasSucceeded.Should().BeFalse();
        httpContext.Items[StoreAccessAuthorizationHandler.FailureCodeItemKey]
            .Should().Be(AdminStoreErrorCodes.ContextStoreMismatch);
    }

    [Fact]
    public async Task Admin_WithAContextForTheRouteStore_ShouldPass()
    {
        var storeId = Guid.NewGuid();
        var httpContext = CreateHttpContext("Admin", Feature(storeId));
        httpContext.Request.RouteValues["storeId"] = storeId.ToString();

        var result = await AuthorizeAsync(httpContext);

        result.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Customer_ShouldAlwaysFail()
    {
        var result = await AuthorizeAsync(CreateHttpContext("Customer"));

        result.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task ResultHandler_ShouldWriteTheStableCode()
    {
        var httpContext = CreateHttpContext("Admin");
        httpContext.Response.Body = new MemoryStream();
        httpContext.Items[StoreAccessAuthorizationHandler.FailureCodeItemKey] =
            AdminStoreErrorCodes.ContextStoreMismatch;

        var handler = new StoreAccessAuthorizationResultHandler();
        await handler.HandleAsync(
            _ => Task.CompletedTask,
            httpContext,
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            PolicyAuthorizationResult.Forbid());

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(AdminStoreErrorCodes.ContextStoreMismatch);
        document.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private async Task<AuthorizationHandlerContext> AuthorizeAsync(HttpContext httpContext)
    {
        var context = new AuthorizationHandlerContext(
            [_requirement],
            httpContext.User,
            httpContext);

        await _handler.HandleAsync(context);
        return context;
    }

    private static AdminStoreContextFeature Feature(Guid storeId) => new()
    {
        ContextId = Guid.NewGuid(),
        AdminUserId = Guid.NewGuid(),
        StoreId = storeId,
        StoreOwnerUserId = Guid.NewGuid(),
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
    };

    private static DefaultHttpContext CreateHttpContext(string role, AdminStoreContextFeature? feature = null)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
                new Claim(ClaimTypes.Role, role)
            ],
            "Test");

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        if (feature is not null)
        {
            context.Features.Set(feature);
        }

        return context;
    }
}
