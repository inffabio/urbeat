using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Urbeat.Application.Security;
using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Security;

/// <summary>
/// Centralizes the delegated-administration audit stamp. When a request carries a valid store
/// context feature, every <see cref="AuditLog"/> row added in the unit of work records the real
/// administrator as actor and the store owner as subject. Outside a delegated request (seller,
/// customer, jobs, seeders, tests) nothing is modified.
/// </summary>
public sealed class AdminStoreAuditStampInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AdminStoreAuditStampInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        var feature = _httpContextAccessor.HttpContext?.Features.Get<AdminStoreContextFeature>();
        if (feature is null)
        {
            return;
        }

        foreach (var entry in dbContext.ChangeTracker
                     .Entries<AuditLog>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            var log = entry.Entity;
            var originalUserId = log.UserId;

            log.ActorUserId ??= feature.AdminUserId;
            if (log.SubjectUserId is null && originalUserId is { } subject && subject != feature.AdminUserId)
            {
                log.SubjectUserId = subject;
            }

            log.AdminStoreContextId ??= feature.ContextId;
            log.UserId = feature.AdminUserId;
        }
    }
}
