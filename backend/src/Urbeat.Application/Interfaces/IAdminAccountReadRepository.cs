using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Read-side projection of the administrator listing. The list is paginated and filtered in SQL;
/// it never loads identities through <c>UserManager.GetUsersInRoleAsync</c>.
/// </summary>
public interface IAdminAccountReadRepository
{
    Task<PagedAdminAdministratorsDto> ListAsync(
        AdminAccountsQueryDto query,
        Guid currentUserId,
        int maxPageSize,
        CancellationToken cancellationToken = default);
}
