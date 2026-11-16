using Microsoft.EntityFrameworkCore;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Persistence.ReadRepositories;

/// <summary>
/// Paginated administrative store listing projected directly from the database. Nothing beyond the
/// store identity, its owner e-mail/name, the publication/open flags and the subscription summary is
/// returned: documents, Pix keys, phone numbers, addresses and operational data stay behind the
/// opened context.
/// </summary>
public sealed class AdminStoreReadRepository : IAdminStoreReadRepository
{
    private const string OwnerNameClaimType = "FullName";

    private readonly ApplicationDbContext _dbContext;

    public AdminStoreReadRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedAdminStoresDto> ListAsync(
        AdminStoresQueryDto query,
        int maxPageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = query.Page < 1 ? 1 : Math.Min(query.Page, 1_000_000);
        var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, Math.Max(maxPageSize, 1));

        var trimmedSearch = query.Search?.Trim();
        var search = string.IsNullOrEmpty(trimmedSearch)
            ? null
            : trimmedSearch[..Math.Min(trimmedSearch.Length, 120)].ToLowerInvariant();

        var stores = _dbContext.Stores.AsNoTracking();

        if (search is not null)
        {
            // ToLower().Contains works on both Npgsql and InMemory (unlike ILike). Broad textual
            // search intentionally has no trigram index in this delivery.
            stores = stores.Where(store =>
                store.Name.ToLower().Contains(search)
                || store.Slug.ToLower().Contains(search)
                || _dbContext.Users.Any(user =>
                    user.Id == store.OwnerUserId
                    && user.Email != null
                    && user.Email.ToLower().Contains(search))
                || _dbContext.UserClaims.Any(claim =>
                    claim.UserId == store.OwnerUserId
                    && claim.ClaimType == OwnerNameClaimType
                    && claim.ClaimValue != null
                    && claim.ClaimValue.ToLower().Contains(search)));
        }

        var status = Normalize(query.Status, "all");
        stores = status switch
        {
            "suspended" => stores.Where(store => store.IsSubscriptionBlocked),
            "onboarding" => stores.Where(store => !store.IsSubscriptionBlocked && !store.IsPublished),
            "active" => stores.Where(store => !store.IsSubscriptionBlocked && store.IsPublished),
            _ => stores
        };

        var publication = Normalize(query.Publication, "all");
        stores = publication switch
        {
            "published" => stores.Where(store => store.IsPublished),
            "draft" => stores.Where(store => !store.IsPublished),
            _ => stores
        };

        var openState = Normalize(query.OpenState, "all");
        stores = openState switch
        {
            "open" => stores.Where(store => store.IsOpen),
            "closed" => stores.Where(store => !store.IsOpen),
            _ => stores
        };

        var subscription = Normalize(query.Subscription, "all");
        stores = subscription switch
        {
            "active" => stores.Where(store => _dbContext.SellerSubscriptionStatuses.Any(row =>
                row.SellerUserId == store.OwnerUserId
                && row.BillingStatus == SellerSubscriptionBillingStatus.Active)),
            "overdue" => stores.Where(store => _dbContext.SellerSubscriptionStatuses.Any(row =>
                row.SellerUserId == store.OwnerUserId
                && row.BillingStatus == SellerSubscriptionBillingStatus.Overdue)),
            "blocked" => stores.Where(store => _dbContext.SellerSubscriptionStatuses.Any(row =>
                row.SellerUserId == store.OwnerUserId
                && row.BillingStatus == SellerSubscriptionBillingStatus.Blocked)),
            "none" => stores.Where(store =>
                !_dbContext.SellerSubscriptionStatuses.Any(row => row.SellerUserId == store.OwnerUserId)),
            _ => stores
        };

        var totalItems = await stores.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var sort = Normalize(query.Sort, "name");
        var ordered = sort switch
        {
            "name_desc" => stores.OrderByDescending(store => store.Name).ThenBy(store => store.Id),
            "created" => stores.OrderBy(store => store.CreatedAtUtc).ThenBy(store => store.Id),
            "created_desc" => stores.OrderByDescending(store => store.CreatedAtUtc).ThenBy(store => store.Id),
            "updated" => stores.OrderBy(store => store.UpdatedAtUtc).ThenBy(store => store.Id),
            "updated_desc" => stores.OrderByDescending(store => store.UpdatedAtUtc).ThenBy(store => store.Id),
            _ => stores.OrderBy(store => store.Name).ThenBy(store => store.Id)
        };

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(store => new
            {
                store.Id,
                store.Name,
                store.Slug,
                store.LogoUrl,
                store.OwnerUserId,
                store.IsPublished,
                store.IsOpen,
                store.IsSubscriptionBlocked,
                store.CreatedAtUtc,
                store.UpdatedAtUtc,
                OwnerEmail = _dbContext.Users
                    .Where(user => user.Id == store.OwnerUserId)
                    .Select(user => user.Email)
                    .FirstOrDefault(),
                OwnerName = _dbContext.UserClaims
                    .Where(claim => claim.UserId == store.OwnerUserId
                                    && claim.ClaimType == OwnerNameClaimType)
                    .Select(claim => claim.ClaimValue)
                    .FirstOrDefault(),
                BillingStatus = _dbContext.SellerSubscriptionStatuses
                    .Where(row => row.SellerUserId == store.OwnerUserId)
                    .Select(row => (SellerSubscriptionBillingStatus?)row.BillingStatus)
                    .FirstOrDefault(),
                NextDueDateUtc = _dbContext.SellerSubscriptionStatuses
                    .Where(row => row.SellerUserId == store.OwnerUserId)
                    .Select(row => (DateTime?)row.NextDueDateUtc)
                    .FirstOrDefault(),
                PlanName = _dbContext.SellerSubscriptions
                    .Where(row => row.StoreId == store.Id)
                    .OrderByDescending(row => row.StartDateUtc)
                    .Select(row => row.PlanName)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new AdminStoreSummaryDto
        {
            Id = row.Id,
            Name = row.Name,
            Slug = row.Slug,
            LogoUrl = row.LogoUrl,
            Owner = new AdminStoreOwnerDto
            {
                UserId = row.OwnerUserId,
                Email = row.OwnerEmail,
                Name = string.IsNullOrWhiteSpace(row.OwnerName) ? row.OwnerEmail : row.OwnerName
            },
            Status = ResolveStatus(row.IsSubscriptionBlocked, row.IsPublished),
            IsPublished = row.IsPublished,
            IsOpen = row.IsOpen,
            IsSubscriptionBlocked = row.IsSubscriptionBlocked,
            Subscription = new AdminStoreSubscriptionDto
            {
                Status = row.BillingStatus?.ToString() ?? "None",
                PlanName = string.IsNullOrWhiteSpace(row.PlanName) ? null : row.PlanName,
                NextDueDateUtc = row.NextDueDateUtc
            },
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc
        }).ToList();

        return new PagedAdminStoresDto
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Items = items
        };
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(normalized) ? fallback : normalized;
    }

    private static string ResolveStatus(bool isSubscriptionBlocked, bool isPublished)
    {
        if (isSubscriptionBlocked)
        {
            return "Suspended";
        }

        return isPublished ? "Active" : "Onboarding";
    }
}
