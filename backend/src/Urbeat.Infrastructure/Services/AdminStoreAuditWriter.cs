using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Writes delegated-administration audit rows in an isolated scope so a denial or an executed
/// mutation never mutates the request's unit of work. The description never contains the token,
/// headers, secrets or payload data.
/// </summary>
public sealed class AdminStoreAuditWriter : IAdminStoreAuditWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminStoreAuditWriter> _logger;

    public AdminStoreAuditWriter(
        IServiceScopeFactory scopeFactory,
        ILogger<AdminStoreAuditWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task TryWriteAsync(
        Guid adminUserId,
        Guid? storeId,
        Guid? ownerUserId,
        Guid? contextId,
        string auditEvent,
        string description,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = adminUserId,
                ActorUserId = adminUserId,
                SubjectUserId = ownerUserId,
                AdminStoreContextId = contextId,
                Event = auditEvent,
                Entity = nameof(AdminStoreAccessContext),
                EntityId = storeId,
                Description = description,
                IpAddress = ipAddress
            });

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Auditing must never break the operation it describes.
            _logger.LogWarning(
                exception,
                "ADMIN_STORE_AUDIT_FAILED | Event={Event} | ActorUserId={ActorUserId} | StoreId={StoreId} | ContextId={ContextId}",
                auditEvent,
                adminUserId,
                storeId,
                contextId);
        }
    }
}
