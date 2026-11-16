using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Persistence.ReadRepositories;

/// <summary>
/// Paginated administrator listing projected directly from the Identity tables plus the
/// administrative state/invitation rows. Ordering is deterministic (normalized e-mail) so page
/// boundaries never repeat or skip an item.
/// </summary>
public sealed class AdminAccountReadRepository : IAdminAccountReadRepository
{
    private const string AdminRole = "Admin";

    private readonly ApplicationDbContext _dbContext;
    private readonly AdminAccountOptions _options;

    public AdminAccountReadRepository(
        ApplicationDbContext dbContext,
        IOptions<AdminAccountOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<PagedAdminAdministratorsDto> ListAsync(
        AdminAccountsQueryDto query,
        Guid currentUserId,
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

        var status = (query.Status ?? "all").Trim().ToLowerInvariant();

        var allAdministrators = from user in _dbContext.Users.AsNoTracking()
                                join userRole in _dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                                join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                where role.Name == AdminRole
                                select user;

        var administrators = allAdministrators;

        if (search is not null)
        {
            administrators = administrators.Where(user =>
                user.Email != null && user.Email.ToLower().Contains(search));
        }

        if (status == "active")
        {
            administrators = administrators.Where(user =>
                user.EmailConfirmed
                && !_dbContext.AdminAccountStates.Any(state => state.UserId == user.Id && state.IsBlocked));
        }
        else if (status == "blocked")
        {
            administrators = administrators.Where(user =>
                user.EmailConfirmed
                && _dbContext.AdminAccountStates.Any(state => state.UserId == user.Id && state.IsBlocked));
        }
        else if (status == "invited")
        {
            administrators = administrators.Where(user => !user.EmailConfirmed);
        }

        var totalItems = await administrators.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var utcNow = DateTime.UtcNow;

        var rows = await administrators
            .OrderBy(user => user.Email)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.EmailConfirmed,
                IsBlocked = _dbContext.AdminAccountStates
                    .Any(state => state.UserId == user.Id && state.IsBlocked),
                InvitationId = _dbContext.AdminInvitations
                    .Where(invitation => invitation.UserId == user.Id
                                         && invitation.Purpose == AdminInvitationPurpose.AccountInvitation
                                         && invitation.ConsumedAtUtc == null
                                         && invitation.RevokedAtUtc == null
                                         && invitation.SupersededAtUtc == null)
                    .OrderByDescending(invitation => invitation.CreatedAtUtc)
                    .Select(invitation => (Guid?)invitation.Id)
                    .FirstOrDefault(),
                InvitationExpiresAtUtc = _dbContext.AdminInvitations
                    .Where(invitation => invitation.UserId == user.Id
                                         && invitation.Purpose == AdminInvitationPurpose.AccountInvitation
                                         && invitation.ConsumedAtUtc == null
                                         && invitation.RevokedAtUtc == null
                                         && invitation.SupersededAtUtc == null)
                    .OrderByDescending(invitation => invitation.CreatedAtUtc)
                    .Select(invitation => (DateTime?)invitation.ExpiresAtUtc)
                    .FirstOrDefault(),
                InvitationEmailSentAtUtc = _dbContext.AdminInvitations
                    .Where(invitation => invitation.UserId == user.Id
                                         && invitation.Purpose == AdminInvitationPurpose.AccountInvitation
                                         && invitation.ConsumedAtUtc == null
                                         && invitation.RevokedAtUtc == null
                                         && invitation.SupersededAtUtc == null)
                    .OrderByDescending(invitation => invitation.CreatedAtUtc)
                    .Select(invitation => invitation.EmailSentAtUtc)
                    .FirstOrDefault(),
                InvitationSendingAtUtc = _dbContext.AdminInvitations
                    .Where(invitation => invitation.UserId == user.Id
                                         && invitation.Purpose == AdminInvitationPurpose.AccountInvitation
                                         && invitation.ConsumedAtUtc == null
                                         && invitation.RevokedAtUtc == null
                                         && invitation.SupersededAtUtc == null)
                    .OrderByDescending(invitation => invitation.CreatedAtUtc)
                    .Select(invitation => invitation.EmailSendingAtUtc)
                    .FirstOrDefault(),
                InvitationCreatedAtUtc = _dbContext.AdminInvitations
                    .Where(invitation => invitation.UserId == user.Id
                                         && invitation.Purpose == AdminInvitationPurpose.AccountInvitation
                                         && invitation.ConsumedAtUtc == null
                                         && invitation.RevokedAtUtc == null
                                         && invitation.SupersededAtUtc == null)
                    .OrderByDescending(invitation => invitation.CreatedAtUtc)
                    .Select(invitation => (DateTime?)invitation.CreatedAtUtc)
                    .FirstOrDefault(),
                // AspNetUsers has no creation timestamp (the Identity model is intentionally not
                // extended). The earliest invitation is the best available approximation; a seeded
                // administrator without an invitation falls back to the Unix epoch and the UI can
                // render it as "—".
                AccountCreatedAtUtc = _dbContext.AdminInvitations
                    .Where(invitation => invitation.UserId == user.Id)
                    .OrderBy(invitation => invitation.CreatedAtUtc)
                    .Select(invitation => (DateTime?)invitation.CreatedAtUtc)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var activeCount = await allAdministrators
            .CountAsync(
                user => user.EmailConfirmed
                        && !_dbContext.AdminAccountStates.Any(
                            state => state.UserId == user.Id && state.IsBlocked),
                cancellationToken);

        var items = rows.Select(row =>
        {
            var isActive = row.EmailConfirmed && !row.IsBlocked;
            AdminInvitationDto? invitation = null;

            if (row.InvitationId is { } invitationId
                && row.InvitationExpiresAtUtc is { } expiresAtUtc
                && row.InvitationCreatedAtUtc is { } createdAtUtc)
            {
                invitation = new AdminInvitationDto
                {
                    Id = invitationId,
                    Purpose = AdminInvitationPurpose.AccountInvitation.ToString(),
                    State = ResolveInvitationState(
                        row.InvitationSendingAtUtc,
                        expiresAtUtc,
                        utcNow).ToString(),                    ExpiresAtUtc = expiresAtUtc,
                    EmailSentAtUtc = row.InvitationEmailSentAtUtc,
                    CreatedAtUtc = createdAtUtc
                };
            }

            return new AdminAdministratorDto
            {
                Id = row.Id,
                Email = row.Email ?? string.Empty,
                Status = !row.EmailConfirmed
                    ? "Invited"
                    : row.IsBlocked
                        ? "Blocked"
                        : "Active",
                IsEmailConfirmed = row.EmailConfirmed,
                IsBlocked = row.IsBlocked,
                IsCurrentUser = row.Id == currentUserId,
                IsLastActiveAdmin = isActive && activeCount == 1,
                CreatedAtUtc = row.AccountCreatedAtUtc ?? DateTime.UnixEpoch,
                Invitation = invitation
            };
        }).ToList();

        return new PagedAdminAdministratorsDto
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Items = items
        };
    }

    private AdminInvitationState ResolveInvitationState(
        DateTime? emailSendingAtUtc,
        DateTime expiresAtUtc,
        DateTime utcNow)
    {
        if (expiresAtUtc <= utcNow)
        {
            return AdminInvitationState.Expired;
        }

        if (emailSendingAtUtc is { } sendingAt && sendingAt >= utcNow - _options.EmailSendLease)
        {
            return AdminInvitationState.Sending;
        }

        return AdminInvitationState.Pending;
    }
}
