using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;

namespace Urbeat.WebApi.Security;

/// <summary>
/// Attaches the delegated store context to the request pipeline. It runs after authentication and
/// before authorization, ignores requests without the header and only processes delegable prefixes
/// (public/anonymous endpoints and administrative/auth routes are never blocked by a stale
/// context).
/// </summary>
public sealed class AdminStoreContextMiddleware
{
    public const string HeaderName = "X-Admin-Store-Context";

    private const string AdminRole = "Admin";

    private static readonly string[] DelegablePrefixes =
    [
        "/api/stores/",
        "/api/orders/",
        "/api/neighborhoods/",
        // Subscription reads are delegated (read-only); the middleware would otherwise leave the
        // StoreAccess policy without a feature and deny the administrator.
        "/api/subscriptions/"
    ];

    private readonly RequestDelegate _next;

    public AdminStoreContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAdminStoreAccessService accessService,
        IAdminStoreAuditWriter auditWriter)
    {
        var header = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(header) || !IsDelegablePath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, null, "Autenticação necessária.");
            return;
        }

        var adminUserId = TryGetUserId(context);
        if (adminUserId is null)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, null, "Autenticação necessária.");
            return;
        }

        if (!context.User.IsInRole(AdminRole))
        {
            await auditWriter.TryWriteAsync(
                adminUserId.Value,
                null,
                null,
                null,
                "AdminStoreAccessRejected",
                "Delegated context header rejected: the session is not an administrator.",
                context.Connection.RemoteIpAddress?.ToString());

            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                AdminStoreErrorCodes.ContextNotAllowed,
                AdminStoreErrorCodes.MessageFor(AdminStoreErrorCodes.ContextNotAllowed));
            return;
        }

        var validation = await accessService.ValidateAsync(
            header,
            adminUserId.Value,
            expectedStoreId: null,
            touch: true,
            context.Connection.RemoteIpAddress?.ToString(),
            context.RequestAborted);

        if (!validation.IsValid)
        {
            var code = MapStatus(validation.Status);
            if (validation.Status is AdminStoreAccessStatus.Invalid)
            {
                await auditWriter.TryWriteAsync(
                    adminUserId.Value,
                    null,
                    null,
                    null,
                    "AdminStoreAccessRejected",
                    "Delegated context header rejected: unknown or tampered token.",
                    context.Connection.RemoteIpAddress?.ToString());
            }

            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, code, AdminStoreErrorCodes.MessageFor(code));
            return;
        }

        context.Features.Set(new AdminStoreContextFeature
        {
            ContextId = validation.ContextId!.Value,
            AdminUserId = validation.AdminUserId!.Value,
            StoreId = validation.StoreId!.Value,
            StoreOwnerUserId = validation.StoreOwnerUserId!.Value,
            ExpiresAtUtc = validation.ExpiresAtUtc!.Value
        });

        await _next(context);
    }

    private static bool IsDelegablePath(PathString path)
    {
        var value = path.Value;
        return !string.IsNullOrEmpty(value)
               && DelegablePrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static Guid? TryGetUserId(HttpContext context)
    {
        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }

    private static string MapStatus(AdminStoreAccessStatus status) => status switch
    {
        AdminStoreAccessStatus.Expired => AdminStoreErrorCodes.ContextExpired,
        AdminStoreAccessStatus.AdminNotAllowed => AdminStoreErrorCodes.ContextAdminNotAllowed,
        AdminStoreAccessStatus.StoreMissing => AdminStoreErrorCodes.ContextStoreMissing,
        AdminStoreAccessStatus.StoreMismatch => AdminStoreErrorCodes.ContextStoreMismatch,
        _ => AdminStoreErrorCodes.ContextInvalid
    };

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string? code,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        if (code is null)
        {
            await context.Response.WriteAsJsonAsync(new { message });
            return;
        }

        await context.Response.WriteAsJsonAsync(new { code, message });
    }
}
