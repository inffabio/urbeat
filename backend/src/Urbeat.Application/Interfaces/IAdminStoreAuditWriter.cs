namespace Urbeat.Application.Interfaces;

/// <summary>
/// Best-effort writer for delegated administrative audit rows. Failures are logged
/// (<c>ADMIN_STORE_AUDIT_FAILED</c>) and never break the operation.
/// </summary>
public interface IAdminStoreAuditWriter
{
    Task TryWriteAsync(
        Guid adminUserId,
        Guid? storeId,
        Guid? ownerUserId,
        Guid? contextId,
        string auditEvent,
        string description,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
