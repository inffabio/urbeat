using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Urbeat.Domain.Entities;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Inserts a credential of unknown origin (no <see cref="UserRoleCredential.RolePromotionRequestId"/>)
/// in the very save that writes the promotion credential. It models a concurrent registration that
/// legitimately created its own credential between the promotion steps. The compensation must remove
/// only the credential identified by the id generated for the promotion attempt, never this one.
/// </summary>
public sealed class InjectCompetingCredentialInterceptor : SaveChangesInterceptor
{
    private int _armed;

    /// <summary>Arms the next save that inserts a promotion credential.</summary>
    public void Arm(string passwordHash)
    {
        PasswordHash = passwordHash;
        Interlocked.Exchange(ref _armed, 1);
    }

    private string PasswordHash { get; set; } = "AQAAAAIAAYagAAAAECOMPETINGHASHVALUE";

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Inject(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Inject(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Inject(DbContextEventData eventData)
    {
        if (Volatile.Read(ref _armed) != 1 || eventData.Context is null)
        {
            return;
        }

        var promotionCredential = eventData.Context.ChangeTracker
            .Entries<UserRoleCredential>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .FirstOrDefault(entity => entity.RolePromotionRequestId is not null);

        if (promotionCredential is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _armed, 0) != 1)
        {
            return;
        }

        eventData.Context.Set<UserRoleCredential>().Add(new UserRoleCredential
        {
            UserId = promotionCredential.UserId,
            Role = promotionCredential.Role,
            PasswordHash = PasswordHash,
            AccessFailedCount = 0,
            LockoutEndUtc = null,
            // No link: this credential did not come from the pending promotion.
            RolePromotionRequestId = null
        });
    }
}
