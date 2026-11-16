using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;

namespace Urbeat.WebApi.Security;

/// <summary>
/// Defense-in-depth audit for delegated mutations. One <c>AdminStoreActionExecuted</c> row is written
/// for successful (2xx) POST/PUT/PATCH/DELETE requests executed under a store context. Business rows
/// (<c>StoreUpdated</c>, <c>ProductCreated</c>, ...) remain written by the services themselves.
/// </summary>
public sealed class AdminStoreAuditActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> MutatingVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        var httpContext = context.HttpContext;
        var feature = httpContext.Features.Get<AdminStoreContextFeature>();
        if (feature is null || !MutatingVerbs.Contains(httpContext.Request.Method))
        {
            return;
        }

        if (!IsSuccess(executed, httpContext))
        {
            return;
        }

        var writer = httpContext.RequestServices.GetService(typeof(IAdminStoreAuditWriter)) as IAdminStoreAuditWriter;
        if (writer is null)
        {
            return;
        }

        var routeTemplate = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? httpContext.Request.Path.Value;
        var statusCode = ResolveStatusCode(executed, httpContext);

        await writer.TryWriteAsync(
            feature.AdminUserId,
            feature.StoreId,
            feature.StoreOwnerUserId,
            feature.ContextId,
            "AdminStoreActionExecuted",
            $"{httpContext.Request.Method} {routeTemplate} {statusCode}",
            httpContext.Connection.RemoteIpAddress?.ToString());
    }

    private static bool IsSuccess(ActionExecutedContext executed, HttpContext httpContext)
        => ResolveStatusCode(executed, httpContext) is >= 200 and < 300;

    private static int ResolveStatusCode(ActionExecutedContext executed, HttpContext httpContext)
        => executed.Result switch
        {
            ObjectResult { StatusCode: { } statusCode } => statusCode,
            StatusCodeResult { StatusCode: var statusCode } => statusCode,
            _ => httpContext.Response.StatusCode == 0
                ? StatusCodes.Status200OK
                : httpContext.Response.StatusCode
        };
}
