using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// One-shot, break-glass recovery of the seeded administrator password. The flow is deliberately
/// narrow: a dedicated external key authorizes it, only the configured admin identity can be
/// targeted, and the key fingerprint is persisted so it cannot be reused. Sessions are revoked and
/// the Identity security stamp is rotated by <see cref="UserManager{TUser}.ResetPasswordAsync"/>.
/// </summary>
public sealed class AdminPasswordRecoveryService : IAdminPasswordRecoveryService
{
    private const string AdminRole = "Admin";

    // InMemory (tests) has no unique constraint and no transactions. This process-wide lock makes
    // the claim check + insert atomic there; production relies on the PostgreSQL unique index and
    // the surrounding transaction instead.
    private static readonly SemaphoreSlim InMemoryClaimLock = new(1, 1);

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly AdminRecoveryOptions _options;
    private readonly AdminSeedOptions _adminSeedOptions;
    private readonly ILogger<AdminPasswordRecoveryService> _logger;

    public AdminPasswordRecoveryService(
        UserManager<IdentityUser<Guid>> userManager,
        IRefreshTokenRepository refreshTokenRepository,
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IOptions<AdminRecoveryOptions> options,
        IOptions<AdminSeedOptions> adminSeedOptions,
        ILogger<AdminPasswordRecoveryService> logger)
    {
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _options = options.Value;
        _adminSeedOptions = adminSeedOptions.Value;
        _logger = logger;
    }

    public bool IsAvailable => _options.IsAvailable;

    public async Task<AdminPasswordRecoveryResultDto> RecoverAsync(
        AdminRecoverPasswordRequestDto request,
        string? providedRecoveryKey,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            // Disabled or missing key: behave as if the route did not exist and do not log request
            // details that could confirm the feature to an unauthenticated caller.
            return Result(AdminPasswordRecoveryStatus.NotAvailable);
        }

        if (!IsProvidedKeyValid(providedRecoveryKey))
        {
            _logger.LogWarning("ADMIN_RECOVERY_INVALID_KEY | IP={IpAddress}", ipAddress);
            await TryWriteAuditAsync(
                userId: null,
                auditEvent: "AdminPasswordRecoveryInvalidKey",
                entityId: null,
                description: "Administrative recovery rejected: invalid recovery key.",
                ipAddress,
                cancellationToken);
            return Result(AdminPasswordRecoveryStatus.InvalidKey);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var configuredEmail = (_adminSeedOptions.Email ?? string.Empty).Trim().ToLowerInvariant();

        // The break-glass route targets exclusively the administrator identity configured for
        // seeding, so a missing configuration rejects every request before any user lookup. There
        // is deliberately no fallback to "an existing Admin account": that would widen the
        // recovery surface beyond the single address the deployment operator declared.
        if (string.IsNullOrEmpty(configuredEmail))
        {
            _logger.LogWarning(
                "ADMIN_RECOVERY_TARGET_REJECTED | Email={Email} | IP={IpAddress} | Reason=AdminSeedEmailNotConfigured",
                normalizedEmail,
                ipAddress);
            await TryWriteAuditAsync(
                userId: null,
                auditEvent: "AdminPasswordRecoveryTargetRejected",
                entityId: null,
                description: "Administrative recovery rejected: no administrator e-mail is configured for recovery.",
                ipAddress,
                cancellationToken);
            return Result(AdminPasswordRecoveryStatus.TargetRejected);
        }

        // Unknown addresses and role mismatches share the same response so this endpoint cannot be
        // used to enumerate accounts.
        if (!string.Equals(normalizedEmail, configuredEmail, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "ADMIN_RECOVERY_TARGET_REJECTED | Email={Email} | IP={IpAddress}",
                normalizedEmail,
                ipAddress);
            await TryWriteAuditAsync(
                userId: null,
                auditEvent: "AdminPasswordRecoveryTargetRejected",
                entityId: null,
                description: $"Administrative recovery rejected for {normalizedEmail}: address is not the configured administrator.",
                ipAddress,
                cancellationToken);
            return Result(AdminPasswordRecoveryStatus.TargetRejected);
        }

        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || !await _userManager.IsInRoleAsync(user, AdminRole))
        {
            _logger.LogWarning(
                "ADMIN_RECOVERY_TARGET_REJECTED | Email={Email} | IP={IpAddress}",
                normalizedEmail,
                ipAddress);
            await TryWriteAuditAsync(
                userId: user?.Id,
                auditEvent: "AdminPasswordRecoveryTargetRejected",
                entityId: user?.Id,
                description: $"Administrative recovery rejected for {normalizedEmail}: the account is missing or is not an administrator.",
                ipAddress,
                cancellationToken);
            return Result(AdminPasswordRecoveryStatus.TargetRejected);
        }

        var passwordValidation = await ValidatePasswordAsync(user, request.NewPassword);
        if (!passwordValidation.Succeeded)
        {
            var error = passwordValidation.Errors.FirstOrDefault()?.Description
                ?? "A nova senha não atende aos requisitos de segurança.";
            _logger.LogWarning(
                "ADMIN_RECOVERY_PASSWORD_REJECTED | UserId={UserId} | IP={IpAddress}",
                user.Id,
                ipAddress);
            await TryWriteAuditAsync(
                user.Id,
                "AdminPasswordRecoveryPasswordRejected",
                user.Id,
                "Administrative recovery rejected: the new password failed Identity validation.",
                ipAddress,
                cancellationToken);
            return Result(AdminPasswordRecoveryStatus.PasswordRejected, error);
        }

        var now = DateTime.UtcNow;
        var usage = new AdminRecoveryKeyUsage
        {
            KeyFingerprint = ComputeFingerprint(_options.Key),
            AdminUserId = user.Id,
            UsedAtUtc = now,
            IpAddress = ipAddress ?? string.Empty
        };

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        bool claimed;
        try
        {
            claimed = await TryClaimKeyAsync(usage, cancellationToken);
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();
            _logger.LogError(
                exception,
                "ADMIN_RECOVERY_FAILED | UserId={UserId} | IP={IpAddress}",
                user.Id,
                ipAddress);
            await TryWriteAuditAsync(
                user.Id,
                "AdminPasswordRecoveryFailed",
                user.Id,
                "Administrative recovery failed while claiming the recovery key.",
                ipAddress,
                cancellationToken);
            return Result(AdminPasswordRecoveryStatus.Failed);
        }

        if (!claimed)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();

            _logger.LogWarning(
                "ADMIN_RECOVERY_KEY_REUSED | UserId={UserId} | IP={IpAddress}",
                user.Id,
                ipAddress);
            await TryWriteAuditAsync(
                user.Id,
                "AdminPasswordRecoveryKeyReused",
                user.Id,
                "Administrative recovery rejected: the recovery key was already used.",
                ipAddress,
                cancellationToken);
            return Result(AdminPasswordRecoveryStatus.KeyAlreadyUsed);
        }

        try
        {
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
            if (!resetResult.Succeeded)
            {
                var error = resetResult.Errors.FirstOrDefault()?.Description
                    ?? "Não foi possível redefinir a senha do administrador.";
                await ReleaseClaimAsync(transaction, usage, cancellationToken);

                _logger.LogWarning(
                    "ADMIN_RECOVERY_PASSWORD_REJECTED | UserId={UserId} | IP={IpAddress}",
                    user.Id,
                    ipAddress);
                await TryWriteAuditAsync(
                    user.Id,
                    "AdminPasswordRecoveryPasswordRejected",
                    user.Id,
                    "Administrative recovery rejected: Identity could not apply the new password.",
                    ipAddress,
                    cancellationToken);
                return Result(AdminPasswordRecoveryStatus.PasswordRejected, error);
            }

            await _userManager.ResetAccessFailedCountAsync(user);
            await _userManager.SetLockoutEndDateAsync(user, null);

            // Admin sessions are issued with a null role, so revoke every context for the user.
            await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, DateTime.UtcNow, cancellationToken);

            _dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = user.Id,
                Event = "AdminPasswordRecovered",
                Entity = nameof(IdentityUser<Guid>),
                EntityId = user.Id,
                Description = $"Administrative password recovered for {normalizedEmail}; all sessions were revoked.",
                IpAddress = ipAddress
            });
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "ADMIN_RECOVERY_SUCCEEDED | UserId={UserId} | Email={Email} | IP={IpAddress}",
                user.Id,
                normalizedEmail,
                ipAddress);
            return Result(AdminPasswordRecoveryStatus.Succeeded);
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();

            // On InMemory there is no transaction to undo the claim; release it so a transient
            // failure does not burn the operator's key. The relational row is already rolled back.
            await ReleaseInMemoryClaimAsync(usage, cancellationToken);

            _logger.LogError(
                exception,
                "ADMIN_RECOVERY_FAILED | UserId={UserId} | IP={IpAddress}",
                user.Id,
                ipAddress);
            await TryWriteAuditAsync(
                user.Id,
                "AdminPasswordRecoveryFailed",
                user.Id,
                "Administrative recovery failed while applying the new password.",
                ipAddress,
                cancellationToken);
            return Result(AdminPasswordRecoveryStatus.Failed);
        }
    }

    private bool IsProvidedKeyValid(string? providedKey)
    {
        if (string.IsNullOrEmpty(providedKey)
            || providedKey.Length < AdminRecoveryOptions.MinimumKeyLength
            || providedKey.Length > AdminRecoveryOptions.MaximumKeyLength)
        {
            return false;
        }

        // Hashing both values normalizes their length before the fixed-time comparison, so a wrong
        // key never leaks how many characters matched. The configured key is never logged.
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(_options.Key));
        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }

    private static string ComputeFingerprint(string configuredKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey)));

    private async Task<bool> TryClaimKeyAsync(AdminRecoveryKeyUsage usage, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsRelational())
        {
            // A single statement combines the unique-index check with the insert. Under concurrent
            // requests only the first transaction inserts; the others block and then get 0 rows.
            var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AdminRecoveryKeyUsages"
                    ("Id", "KeyFingerprint", "AdminUserId", "UsedAtUtc", "IpAddress", "CreatedAtUtc")
                VALUES
                    ({usage.Id}, {usage.KeyFingerprint}, {usage.AdminUserId}, {usage.UsedAtUtc}, {usage.IpAddress}, {usage.CreatedAtUtc})
                ON CONFLICT ("KeyFingerprint") DO NOTHING
                """, cancellationToken);

            return affected == 1;
        }

        await InMemoryClaimLock.WaitAsync(cancellationToken);
        try
        {
            var alreadyUsed = await _dbContext.AdminRecoveryKeyUsages
                .AsNoTracking()
                .AnyAsync(x => x.KeyFingerprint == usage.KeyFingerprint, cancellationToken);
            if (alreadyUsed)
            {
                return false;
            }

            _dbContext.AdminRecoveryKeyUsages.Add(usage);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            InMemoryClaimLock.Release();
        }
    }

    private async Task ReleaseClaimAsync(
        IDbContextTransaction? transaction,
        AdminRecoveryKeyUsage usage,
        CancellationToken cancellationToken)
    {
        await RollBackAsync(transaction, cancellationToken);
        _dbContext.ChangeTracker.Clear();
        await ReleaseInMemoryClaimAsync(usage, cancellationToken);
    }

    private async Task ReleaseInMemoryClaimAsync(AdminRecoveryKeyUsage usage, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsRelational())
        {
            return;
        }

        await InMemoryClaimLock.WaitAsync(cancellationToken);
        try
        {
            var stored = await _dbContext.AdminRecoveryKeyUsages
                .FirstOrDefaultAsync(x => x.Id == usage.Id, cancellationToken);
            if (stored is not null)
            {
                _dbContext.AdminRecoveryKeyUsages.Remove(stored);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            InMemoryClaimLock.Release();
        }
    }

    private static async Task RollBackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch
        {
            // A broken connection leaves PostgreSQL to roll back on dispose. Never mask the original
            // failure with a rollback exception.
        }
    }

    private async Task TryWriteAuditAsync(
        Guid? userId,
        string auditEvent,
        Guid? entityId,
        string description,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Event = auditEvent,
                Entity = nameof(IdentityUser<Guid>),
                EntityId = entityId,
                Description = description,
                IpAddress = ipAddress
            });
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Failure auditing must not change the security outcome returned to the caller.
            _logger.LogWarning(exception, "ADMIN_RECOVERY_AUDIT_FAILED | Event={Event}", auditEvent);
        }
    }

    private async Task<IdentityResult> ValidatePasswordAsync(IdentityUser<Guid> user, string password)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in _userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_userManager, user, password);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed(errors.ToArray());
    }

    private static AdminPasswordRecoveryResultDto Result(
        AdminPasswordRecoveryStatus status,
        string? error = null) => new()
    {
        Status = status,
        Error = error
    };
}
