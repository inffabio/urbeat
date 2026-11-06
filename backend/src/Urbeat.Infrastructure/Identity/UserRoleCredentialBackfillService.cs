using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Urbeat.Application.Security;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Identity;

public sealed record UserRoleCredentialBackfillResult(
    int Created,
    int SkippedExisting,
    int SkippedMissingPassword);

/// <summary>
/// Copies the legacy <c>AspNetUsers.PasswordHash</c> into one <see cref="UserRoleCredential"/>
/// per credential role (Customer/Seller) at startup, after migrations. Idempotent: existing
/// credentials are never touched. Admin/DeliveryDriver never receive a row.
///
/// Users without a legacy hash cannot authenticate with a password anyway; they are logged and
/// left untouched (no account is deleted). Any persistence failure propagates so the host can
/// abort startup instead of running with an inconsistent credential store.
/// </summary>
public sealed class UserRoleCredentialBackfillService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<UserRoleCredentialBackfillService> _logger;

    public UserRoleCredentialBackfillService(
        ApplicationDbContext dbContext,
        ILogger<UserRoleCredentialBackfillService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<UserRoleCredentialBackfillResult> BackfillAsync(CancellationToken cancellationToken = default)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .Select(user => new { user.Id, user.PasswordHash })
            .ToListAsync(cancellationToken);

        var rolesByUser = (await (
                from userRole in _dbContext.UserRoles.AsNoTracking()
                join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .GroupBy(row => row.UserId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Name!).ToArray());

        var existing = (await _dbContext.UserRoleCredentials
                .AsNoTracking()
                .Select(credential => new { credential.UserId, credential.Role })
                .ToListAsync(cancellationToken))
            .Select(row => (row.UserId, row.Role))
            .ToHashSet();

        var created = 0;
        var skippedExisting = 0;
        var skippedMissingPassword = 0;

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        foreach (var user in users)
        {
            if (!rolesByUser.TryGetValue(user.Id, out var roleNames))
            {
                continue;
            }

            var credentialRoles = roleNames
                .Select(roleName => AccountRoles.TryParseCredentialRole(roleName, out var role) ? role : (UserRoleType?)null)
                .Where(role => role.HasValue)
                .Select(role => role!.Value)
                .Distinct()
                .ToArray();

            if (credentialRoles.Length == 0)
            {
                // Admin-only (or delivery-driver-only) identity: intentionally no credential row.
                continue;
            }

            if (credentialRoles.All(role => existing.Contains((user.Id, role))))
            {
                skippedExisting += credentialRoles.Length;
                continue;
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                // Without a legacy hash there is no password to copy. Keep the account and
                // surface the inconsistency instead of silently creating an unusable credential.
                skippedMissingPassword += credentialRoles.Length;
                _logger.LogWarning(
                    "User {UserId} has credential roles {Roles} but no legacy password hash; no UserRoleCredential was created. The account is preserved and requires manual credential remediation.",
                    user.Id,
                    string.Join(", ", credentialRoles));
                continue;
            }

            foreach (var role in credentialRoles)
            {
                if (existing.Contains((user.Id, role)))
                {
                    skippedExisting++;
                    continue;
                }

                _dbContext.UserRoleCredentials.Add(new UserRoleCredential
                {
                    UserId = user.Id,
                    Role = role,
                    PasswordHash = user.PasswordHash,
                    AccessFailedCount = 0,
                    LockoutEndUtc = null
                });
                existing.Add((user.Id, role));
                created++;
            }
        }

        if (created > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "UserRoleCredential backfill finished: {Created} created, {SkippedExisting} already existed, {SkippedMissingPassword} skipped for missing legacy password.",
            created,
            skippedExisting,
            skippedMissingPassword);

        return new UserRoleCredentialBackfillResult(created, skippedExisting, skippedMissingPassword);
    }
}
