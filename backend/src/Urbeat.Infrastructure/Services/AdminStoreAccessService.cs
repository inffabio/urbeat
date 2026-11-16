using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Security;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Authoritative store-context lifecycle. Tokens are 256-bit random values persisted only as their
/// versioned SHA-256 digest; the raw value is returned exactly once. Every state transition audits
/// the actor, the store and the context id, never the token.
/// </summary>
public sealed class AdminStoreAccessService : IAdminStoreAccessService
{
    private const string AdminRole = "Admin";
    private const string DefaultRevokedReason = "Closed";
    private static readonly SemaphoreSlim CreationGate = new(1, 1);

    private readonly ApplicationDbContext _dbContext;
    private readonly ITokenHasher _tokenHasher;
    private readonly TimeProvider _timeProvider;
    private readonly AdminStoreAccessOptions _options;
    private readonly ILogger<AdminStoreAccessService> _logger;

    public AdminStoreAccessService(
        ApplicationDbContext dbContext,
        ITokenHasher tokenHasher,
        TimeProvider timeProvider,
        IOptions<AdminStoreAccessOptions> options,
        ILogger<AdminStoreAccessService> logger)
    {
        _dbContext = dbContext;
        _tokenHasher = tokenHasher;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdminStoreAccessCreationResult> CreateAsync(
        Guid adminUserId,
        Guid storeId,
        string? reason,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (adminUserId == Guid.Empty)
        {
            return AdminStoreAccessCreationResult.Failed(AdminStoreAccessStatus.Invalid);
        }

        if (!await IsAdminAsync(adminUserId, cancellationToken))
        {
            return AdminStoreAccessCreationResult.Failed(AdminStoreAccessStatus.AdminNotAllowed);
        }

        // In-process serialization covers the InMemory test provider and a single API instance.
        // Relational deployments additionally wrap the state change in a transaction; the unique
        // TokenHash index keeps duplicate tokens impossible.
        await CreationGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = _dbContext.Database.IsRelational()
                ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            // Relational replicas serialize per administrator so the active-context limit holds
            // across processes (the in-process gate above only covers one instance).
            await AcquireAdminCreationLockAsync(adminUserId, cancellationToken);

            var store = await _dbContext.Stores
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == storeId, cancellationToken);

            if (store is null)
            {
                return AdminStoreAccessCreationResult.Failed(AdminStoreAccessStatus.StoreNotFound);
            }

            var ownerExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(x => x.Id == store.OwnerUserId, cancellationToken);

            if (!ownerExists)
            {
                return AdminStoreAccessCreationResult.Failed(AdminStoreAccessStatus.StoreOwnerNotFound);
            }

            var utcNow = UtcNow();
            await CleanupRetainedAsync(adminUserId, utcNow, cancellationToken);

            var active = await _dbContext.AdminStoreAccessContexts
                .Where(x => x.AdminUserId == adminUserId && x.RevokedAtUtc == null)
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var expired in active.Where(x => x.ExpiresAtUtc <= utcNow))
            {
                Revoke(expired, "Expired", utcNow);
                WriteAudit(expired, "AdminStoreAccessExpired", "Context expired by absolute lifetime.", ipAddress);
            }

            var stillActive = active.Where(x => x.RevokedAtUtc == null).ToList();
            var overflow = stillActive.Count - _options.MaxActiveContextsPerAdmin + 1;
            if (overflow > 0)
            {
                foreach (var oldest in stillActive.Take(overflow))
                {
                    Revoke(oldest, "Limit", utcNow);
                    WriteAudit(oldest, "AdminStoreAccessRevoked", "Context revoked: active-context limit reached.", ipAddress);
                }
            }

            var rawToken = CreateToken();
            var context = new AdminStoreAccessContext
            {
                AdminUserId = adminUserId,
                StoreId = store.Id,
                TokenHash = _tokenHasher.Hash(rawToken),
                ExpiresAtUtc = utcNow.AddMinutes(_options.ContextLifetimeMinutes),
                LastSeenAtUtc = utcNow,
                Reason = SanitizeReason(reason),
                IpAddress = Truncate(ipAddress, 64),
                UserAgent = Truncate(userAgent, 256)
            };

            _dbContext.AdminStoreAccessContexts.Add(context);

            WriteAudit(
                context,
                "AdminStoreAccessGranted",
                context.Reason is null
                    ? "Administrative store context opened."
                    : $"Administrative store context opened. Reason: {context.Reason}",
                ipAddress,
                store.OwnerUserId);

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new AdminStoreAccessCreationResult(
                AdminStoreAccessStatus.Valid,
                new AdminStoreAccessContextDto
                {
                    ContextId = context.Id,
                    AdminUserId = adminUserId,
                    StoreId = store.Id,
                    StoreName = store.Name,
                    StoreSlug = store.Slug,
                    ExpiresAtUtc = context.ExpiresAtUtc,
                    Token = rawToken
                });
        }
        finally
        {
            CreationGate.Release();
        }
    }

    public async Task<AdminStoreAccessValidation> ValidateAsync(
        string? token,
        Guid adminUserId,
        Guid? expectedStoreId,
        bool touch,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedToken = token?.Trim();
        if (string.IsNullOrEmpty(normalizedToken) || adminUserId == Guid.Empty)
        {
            return AdminStoreAccessValidation.Failed(AdminStoreAccessStatus.Invalid);
        }

        var tokenHash = _tokenHasher.Hash(normalizedToken);
        var context = await _dbContext.AdminStoreAccessContexts
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        // A context that does not exist or belongs to another administrator is indistinguishable
        // from a tampered token.
        if (context is null || context.AdminUserId != adminUserId)
        {
            return AdminStoreAccessValidation.Failed(AdminStoreAccessStatus.Invalid);
        }

        var utcNow = UtcNow();

        if (context.RevokedAtUtc is not null)
        {
            return AdminStoreAccessValidation.Failed(MapRevokedReason(context.RevokedReason));
        }

        if (context.ExpiresAtUtc <= utcNow)
        {
            Revoke(context, "Expired", utcNow);
            WriteAudit(context, "AdminStoreAccessExpired", "Context expired by absolute lifetime.", ipAddress);
            await TrySaveAsync(cancellationToken);
            return AdminStoreAccessValidation.Failed(AdminStoreAccessStatus.Expired);
        }

        if (!await IsAdminAsync(adminUserId, cancellationToken))
        {
            Revoke(context, "AdminRoleRemoved", utcNow);
            WriteAudit(context, "AdminStoreAccessRejected", "Actor lost the Admin role.", ipAddress);
            await TrySaveAsync(cancellationToken);
            return AdminStoreAccessValidation.Failed(AdminStoreAccessStatus.AdminNotAllowed);
        }

        var store = await _dbContext.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == context.StoreId, cancellationToken);

        if (store is null)
        {
            Revoke(context, "StoreDeleted", utcNow);
            WriteAudit(context, "AdminStoreAccessRejected", "Store of the context no longer exists.", ipAddress);
            await TrySaveAsync(cancellationToken);
            return AdminStoreAccessValidation.Failed(AdminStoreAccessStatus.StoreMissing);
        }

        if (expectedStoreId is { } expected && expected != context.StoreId)
        {
            return new AdminStoreAccessValidation(
                AdminStoreAccessStatus.StoreMismatch,
                context.Id,
                adminUserId,
                context.StoreId,
                store.OwnerUserId,
                context.ExpiresAtUtc,
                store.Name,
                store.Slug);
        }

        if (touch
            && (context.LastSeenAtUtc is null
                || context.LastSeenAtUtc.Value <= utcNow.AddSeconds(-_options.TouchIntervalSeconds)))
        {
            context.LastSeenAtUtc = utcNow;
            await TrySaveAsync(cancellationToken);
        }

        return new AdminStoreAccessValidation(
            AdminStoreAccessStatus.Valid,
            context.Id,
            adminUserId,
            context.StoreId,
            store.OwnerUserId,
            context.ExpiresAtUtc,
            store.Name,
            store.Slug);
    }

    public async Task<AdminStoreAccessStatus> CloseAsync(
        string? token,
        Guid adminUserId,
        Guid storeId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedToken = token?.Trim();
        if (string.IsNullOrEmpty(normalizedToken) || adminUserId == Guid.Empty)
        {
            return AdminStoreAccessStatus.Invalid;
        }

        var tokenHash = _tokenHasher.Hash(normalizedToken);
        var context = await _dbContext.AdminStoreAccessContexts
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (context is null || context.AdminUserId != adminUserId)
        {
            return AdminStoreAccessStatus.Invalid;
        }

        if (context.StoreId != storeId)
        {
            return AdminStoreAccessStatus.StoreMismatch;
        }

        if (context.RevokedAtUtc is not null)
        {
            // Idempotent close for a known context: no new audit row is produced for the same
            // already-revoked context.
            return AdminStoreAccessStatus.AlreadyClosed;
        }

        Revoke(context, DefaultRevokedReason, UtcNow());
        WriteAudit(context, "AdminStoreAccessRevoked", "Context closed by the administrator.", ipAddress);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return AdminStoreAccessStatus.Valid;
    }

    public async Task RevokeAllAsync(
        Guid adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var utcNow = UtcNow();
            var contexts = await _dbContext.AdminStoreAccessContexts
                .Where(x => x.AdminUserId == adminUserId && x.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);

            if (contexts.Count == 0)
            {
                return;
            }

            foreach (var context in contexts)
            {
                Revoke(context, reason, utcNow);
                WriteAudit(context, "AdminStoreAccessRevoked", $"Context revoked: {reason}.", null);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "ADMIN_STORE_AUDIT_FAILED | Revoking store contexts failed for {AdminUserId}.", adminUserId);
        }
    }

    private async Task CleanupRetainedAsync(Guid adminUserId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var cutoff = utcNow.AddHours(-_options.ExpiredRetentionHours);
        var retained = await _dbContext.AdminStoreAccessContexts
            .Where(x => x.AdminUserId == adminUserId && x.RevokedAtUtc != null && x.RevokedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (retained.Count > 0)
        {
            _dbContext.AdminStoreAccessContexts.RemoveRange(retained);
        }
    }

    /// <summary>
    /// PostgreSQL-only advisory lock scoped to the current transaction. Two replicas opening
    /// contexts for the same administrator cannot both observe the same active count; the lock is
    /// released on commit/rollback. Other providers keep the in-process gate.
    /// </summary>
    private async Task AcquireAdminCreationLockAsync(Guid adminUserId, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational()
            || _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        var lockKey = BitConverter.ToInt64(
            System.Security.Cryptography.SHA256.HashData(adminUserId.ToByteArray()),
            0);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
    }

    private async Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await (from userRole in _dbContext.UserRoles
                      join role in _dbContext.Roles on userRole.RoleId equals role.Id
                      where userRole.UserId == userId && role.Name == AdminRole
                      select 1).AnyAsync(cancellationToken);
    }

    private async Task TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "ADMIN_STORE_AUDIT_FAILED | Persisting the store context state failed.");
        }
    }

    private void Revoke(AdminStoreAccessContext context, string reason, DateTime utcNow)
    {
        context.RevokedAtUtc = utcNow;
        context.RevokedReason = reason;
    }

    private void WriteAudit(
        AdminStoreAccessContext context,
        string auditEvent,
        string description,
        string? ipAddress,
        Guid? ownerUserId = null)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            // The interceptor only stamps rows added under a live request feature. Context lifecycle
            // rows always set the actor/subject explicitly so they are meaningful in every path.
            UserId = context.AdminUserId,
            ActorUserId = context.AdminUserId,
            SubjectUserId = ownerUserId,
            AdminStoreContextId = context.Id,
            Event = auditEvent,
            Entity = nameof(AdminStoreAccessContext),
            EntityId = context.StoreId,
            Description = description,
            IpAddress = Truncate(ipAddress, 64)
        });
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static AdminStoreAccessStatus MapRevokedReason(string? reason) => reason switch
    {
        "Expired" => AdminStoreAccessStatus.Expired,
        "AdminRoleRemoved" => AdminStoreAccessStatus.AdminNotAllowed,
        "StoreDeleted" => AdminStoreAccessStatus.StoreMissing,
        _ => AdminStoreAccessStatus.Invalid
    };

    private static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var sanitized = new string(reason
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();

        return sanitized.Length == 0 ? null : Truncate(sanitized, 200);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
