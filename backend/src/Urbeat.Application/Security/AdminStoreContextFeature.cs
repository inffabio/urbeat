namespace Urbeat.Application.Security;

/// <summary>
/// Request-scoped feature attached by <c>AdminStoreContextMiddleware</c> only after the opaque
/// header token was resolved against a live, non-revoked, non-expired context owned by the current
/// administrator and pointing to an existing store.
/// </summary>
public sealed class AdminStoreContextFeature
{
    public required Guid ContextId { get; init; }

    public required Guid AdminUserId { get; init; }

    public required Guid StoreId { get; init; }

    public required Guid StoreOwnerUserId { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }
}
