using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;

namespace Urbeat.WebApi.Security;

/// <summary>
/// Translates a <see cref="AuthorizationPolicies.StoreAccess"/> failure into a stable
/// <c>{ code, message }</c> payload (converted to ProblemDetails by the existing middleware) and
/// audits the denial. Every other authorization failure keeps the framework default behaviour.
/// </summary>
public sealed class StoreAccessAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            var hasCode = context.Items.TryGetValue(
                StoreAccessAuthorizationHandler.FailureCodeItemKey,
                out var value);

            if (hasCode && value is string code)
            {
                await AuditDenialAsync(context, code);
                await WriteProblemAsync(context, code);
                return;
            }

            // A live delegated context that reaches a seller-only endpoint is out of the approved
            // scope: report the stable action code instead of a generic 403.
            var feature = context.Features.Get<AdminStoreContextFeature>();
            if (feature is not null && context.User.IsInRole("Admin"))
            {
                await AuditDenialAsync(context, AdminStoreErrorCodes.ActionNotAllowed);
                await WriteProblemAsync(context, AdminStoreErrorCodes.ActionNotAllowed);
                return;
            }
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task AuditDenialAsync(HttpContext context, string code)
    {
        var actorUserId = StoreAccessAuthorizationHandler.TryGetAdminUserId(context);
        if (actorUserId is not { } adminUserId)
        {
            return;
        }

        var feature = context.Features.Get<AdminStoreContextFeature>();
        var services = context.RequestServices;
        var writer = services?.GetService(typeof(IAdminStoreAuditWriter)) as IAdminStoreAuditWriter;
        if (writer is null)
        {
            return;
        }

        await writer.TryWriteAsync(
            adminUserId,
            feature?.StoreId,
            feature?.StoreOwnerUserId,
            feature?.ContextId,
            "AdminStoreActionDenied",
            $"Store access denied: {code}.",
            context.Connection.RemoteIpAddress?.ToString());
    }

    internal static Task WriteProblemAsync(HttpContext context, string code)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(new
        {
            code,
            message = AdminStoreErrorCodes.MessageFor(code)
        });
    }
}
