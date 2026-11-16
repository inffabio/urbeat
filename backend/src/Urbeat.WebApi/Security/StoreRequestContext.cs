using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;

namespace Urbeat.WebApi.Security;

/// <summary>
/// Scoped accessor for the effective store identity. In a delegated request it reads the feature
/// attached by the middleware; otherwise it falls back to the authenticated claim exactly as before.
/// Values are never read from the request body or query string.
/// </summary>
public sealed class StoreRequestContext : IStoreRequestContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public StoreRequestContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private AdminStoreContextFeature? Feature
        => _httpContextAccessor.HttpContext?.Features.Get<AdminStoreContextFeature>();

    public Guid? ActorUserId => Feature?.AdminUserId ?? CurrentClaimUserId;

    public Guid? EffectiveSellerUserId => Feature?.StoreOwnerUserId ?? CurrentClaimUserId;

    public Guid? StoreId => Feature?.StoreId;

    public bool IsAdminDelegated => Feature is not null;

    public Guid? ContextId => Feature?.ContextId;

    private Guid? CurrentClaimUserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var subject = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(subject, out var userId) ? userId : null;
        }
    }
}
