using Urbeat.Application.DTOs.Admin;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// The effective store identity of the current request. A seller session resolves it from the
/// authenticated claim; an administrator only gets a value while a valid delegated store context is
/// attached by the middleware. It is the single source the controllers use instead of reading the
/// claim directly.
/// </summary>
public interface IStoreRequestContext
{
    Guid? ActorUserId { get; }

    Guid? EffectiveSellerUserId { get; }

    Guid? StoreId { get; }

    bool IsAdminDelegated { get; }

    Guid? ContextId { get; }
}

/// <summary>
/// Lifecycle and validation of administrative store access contexts. The service is the only place
/// that knows how to hash, resolve, expire and revoke the opaque token; it is used by the admin
/// controller (create/read/close) and by the request middleware (validation).
/// </summary>
public interface IAdminStoreAccessService
{
    Task<AdminStoreAccessCreationResult> CreateAsync(
        Guid adminUserId,
        Guid storeId,
        string? reason,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the header token for the given actor. When <paramref name="touch"/> is true the
    /// <c>LastSeenAtUtc</c> column is refreshed at most once per configured interval.
    /// </summary>
    Task<AdminStoreAccessValidation> ValidateAsync(
        string? token,
        Guid adminUserId,
        Guid? expectedStoreId,
        bool touch,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the presented context. Idempotent for a known context already revoked or expired;
    /// an unknown/tampered token is reported as invalid.
    /// </summary>
    Task<AdminStoreAccessStatus> CloseAsync(
        string? token,
        Guid adminUserId,
        Guid storeId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes every active context of the administrator (server-side logout).</summary>
    Task RevokeAllAsync(
        Guid adminUserId,
        string reason,
        CancellationToken cancellationToken = default);
}
