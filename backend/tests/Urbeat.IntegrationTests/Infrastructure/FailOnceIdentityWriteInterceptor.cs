using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Urbeat.Domain.Entities;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>Promotion step that should fail when the interceptor is armed.</summary>
public enum RolePromotionFailurePoint
{
    /// <summary>Fails while the Identity role row is being inserted.</summary>
    RoleAssignment,

    /// <summary>Fails while the user row is updated to mark the e-mail as confirmed.</summary>
    EmailConfirmationUpdate,

    /// <summary>Fails while the promotion credential row is being removed by the compensation.</summary>
    CompensationCredentialRemoval,

    /// <summary>Fails while the Identity role row is being removed by the compensation.</summary>
    CompensationRoleRemoval
}

/// <summary>
/// Injects persistence failures at specific promotion steps on the InMemory provider, where there is
/// no transaction to roll back. Used to prove that role, credential, FullName claim and EmailConfirmed
/// are compensated, that an incomplete compensation is reported instead of assumed, and that the
/// retry repairs the leftover state and applies the promotion exactly once.
/// </summary>
public sealed class FailOnceIdentityWriteInterceptor : SaveChangesInterceptor
{
    private readonly RolePromotionFailurePoint _failurePoint;
    private readonly Dictionary<RolePromotionFailurePoint, int> _remainingFailures = new();
    private readonly object _sync = new();

    public FailOnceIdentityWriteInterceptor(RolePromotionFailurePoint failurePoint)
    {
        _failurePoint = failurePoint;
    }

    /// <summary>Arms a single failure for the step configured in the constructor.</summary>
    public void Arm() => Arm(_failurePoint);

    /// <summary>Arms <paramref name="times"/> failures for a step (consumed one save at a time).</summary>
    public void Arm(RolePromotionFailurePoint failurePoint, int times = 1)
    {
        lock (_sync)
        {
            _remainingFailures[failurePoint] = Math.Max(1, times);
        }
    }

    /// <summary>Arms a step so that it keeps failing until the test disarms it.</summary>
    public void ArmUntilDisarmed(RolePromotionFailurePoint failurePoint) => Arm(failurePoint, int.MaxValue);

    /// <summary>Stops failing the given step.</summary>
    public void Disarm(RolePromotionFailurePoint failurePoint)
    {
        lock (_sync)
        {
            _remainingFailures.Remove(failurePoint);
        }
    }

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
        var entries = eventData.Context?.ChangeTracker.Entries().ToList();
        if (entries is null || entries.Count == 0)
        {
            return;
        }

        foreach (var failurePoint in Enum.GetValues<RolePromotionFailurePoint>())
        {
            if (!Matches(failurePoint, entries) || !TryConsumeFailure(failurePoint))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Simulated persistence failure during role promotion step {failurePoint}.");
        }
    }

    private static bool Matches(RolePromotionFailurePoint failurePoint, IReadOnlyList<EntityEntry> entries)
        => failurePoint switch
        {
            RolePromotionFailurePoint.RoleAssignment => entries.Any(entry =>
                entry.Entity is IdentityUserRole<Guid> && entry.State == EntityState.Added),

            // AddToRoleAsync also updates the user row, but on an unconfirmed identity EmailConfirmed
            // is still false there. Only the explicit confirmation update has it set to true.
            RolePromotionFailurePoint.EmailConfirmationUpdate => entries.Any(entry =>
                entry.Entity is IdentityUser<Guid> user
                && entry.State == EntityState.Modified
                && user.EmailConfirmed),

            RolePromotionFailurePoint.CompensationCredentialRemoval => entries.Any(entry =>
                entry.Entity is UserRoleCredential && entry.State == EntityState.Deleted),

            RolePromotionFailurePoint.CompensationRoleRemoval => entries.Any(entry =>
                entry.Entity is IdentityUserRole<Guid> && entry.State == EntityState.Deleted),

            _ => false
        };

    private bool TryConsumeFailure(RolePromotionFailurePoint failurePoint)
    {
        lock (_sync)
        {
            if (!_remainingFailures.TryGetValue(failurePoint, out var remaining) || remaining <= 0)
            {
                return false;
            }

            if (remaining != int.MaxValue)
            {
                _remainingFailures[failurePoint] = remaining - 1;
            }

            return true;
        }
    }
}
