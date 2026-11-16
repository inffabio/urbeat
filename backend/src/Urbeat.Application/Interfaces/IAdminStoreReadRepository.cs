using Urbeat.Application.DTOs.Admin;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Read-side projection of the administrative store listing. No domain entity is materialized and
/// no operational or identity secret is exposed: only the owner e-mail/name, publication state and
/// subscription summary are returned.
/// </summary>
public interface IAdminStoreReadRepository
{
    Task<PagedAdminStoresDto> ListAsync(
        AdminStoresQueryDto query,
        int maxPageSize,
        CancellationToken cancellationToken = default);
}
