using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Urbeat.Domain.Entities;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Simulates a persistence failure exactly while the promotion credential is being written, i.e.
/// between the challenge claim and the end of the promotion. Used to prove the challenge stays
/// retryable and that a successful retry never duplicates role/credential.
/// </summary>
public sealed class FailOnceCredentialWriteInterceptor : SaveChangesInterceptor
{
    private int _armed;

    /// <summary>Arms a single failure for the next save that inserts a role credential.</summary>
    public void Arm() => Interlocked.Exchange(ref _armed, 1);

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ThrowIfArmed(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ThrowIfArmed(DbContextEventData eventData)
    {
        var isWritingCredential = eventData.Context?.ChangeTracker
            .Entries<UserRoleCredential>()
            .Any(entry => entry.State == EntityState.Added) == true;

        if (isWritingCredential && Interlocked.Exchange(ref _armed, 0) == 1)
        {
            throw new InvalidOperationException(
                "Simulated persistence failure between the promotion claim and the credential write.");
        }
    }
}
