using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Urbeat.Infrastructure.Security;

namespace Urbeat.WebApi.Security;

/// <summary>
/// Authorizes the <see cref="AuthorizationPolicies.StoreAccess"/> policy. Without a delegated
/// context the historical seller role is required; with a context the actor must still be an Admin
/// and any route/query store id must match the context store. Failures are fail-closed.
/// </summary>
public sealed class StoreAccessAuthorizationHandler : AuthorizationHandler<StoreAccessRequirement>
{
    /// <summary><see cref="HttpContext.Items"/> key carrying the stable error code for the result handler.</summary>
    public const string FailureCodeItemKey = "Urbeat.AdminStoreAccess.FailureCode";

    private const string AdminRole = "Admin";
    private const string SellerRole = "Seller";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public StoreAccessAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StoreAccessRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext ?? _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return Task.CompletedTask;
        }

        var feature = httpContext.Features.Get<AdminStoreContextFeature>();

        if (feature is null)
        {
            if (httpContext.User.IsInRole(SellerRole))
            {
                context.Succeed(requirement);
            }
            else if (httpContext.User.IsInRole(AdminRole))
            {
                // An administrator never inherits seller ownership: without a live context the
                // delegated surface stays closed.
                SetFailure(httpContext, AdminStoreErrorCodes.ContextInvalid);
            }

            return Task.CompletedTask;
        }

        if (!httpContext.User.IsInRole(AdminRole))
        {
            SetFailure(httpContext, AdminStoreErrorCodes.ContextNotAllowed);
            return Task.CompletedTask;
        }

        if (TryResolveStoreId(httpContext, out var routeStoreId) && routeStoreId != feature.StoreId)
        {
            SetFailure(httpContext, AdminStoreErrorCodes.ContextStoreMismatch);
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private static void SetFailure(HttpContext httpContext, string code)
        => httpContext.Items[FailureCodeItemKey] = code;

    private static bool TryResolveStoreId(HttpContext httpContext, out Guid storeId)
    {
        storeId = Guid.Empty;

        if (httpContext.Request.RouteValues.TryGetValue("storeId", out var routeValue)
            && Guid.TryParse(routeValue?.ToString(), out storeId))
        {
            return true;
        }

        if (httpContext.Request.Query.TryGetValue("storeId", out var queryValue)
            && Guid.TryParse(queryValue.ToString(), out storeId))
        {
            return true;
        }

        return false;
    }

    internal static Guid? TryGetAdminUserId(HttpContext httpContext)
    {
        var subject = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
