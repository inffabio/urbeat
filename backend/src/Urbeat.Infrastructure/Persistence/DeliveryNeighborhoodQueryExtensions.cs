using Microsoft.EntityFrameworkCore;
using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Persistence;

/// <summary>
/// Query filters shared by every read path that exposes delivery neighborhoods to selection.
/// </summary>
/// <remarks>
/// The alias rule lives here so the city list, the store radius endpoint and the geographic search
/// cannot drift apart: the store radius path once loaded bounding-box candidates directly and kept
/// returning alias sources next to the canonical neighborhood. The filter only composes
/// <see cref="Queryable.Where{TSource}(IQueryable{TSource}, System.Linq.Expressions.Expression{Func{TSource, bool}})"/>
/// over context sets, so EF Core translates the exclusion to PostgreSQL as a correlated EXISTS
/// instead of filtering in memory.
/// </remarks>
public static class DeliveryNeighborhoodQueryExtensions
{
    /// <summary>
    /// Hides rows that are registered as an active, approved alias source of the same city. The row
    /// remains in the database (and keeps its identifier) for audit, but it is a spelling of the
    /// canonical neighborhood and must not be offered as a separate option. The canonical row is
    /// always kept: the predicate compares <see cref="DeliveryNeighborhoodAlias.CanonicalNeighborhoodId"/>
    /// with the row identifier, so a row that is its own alias target is never excluded.
    /// </summary>
    public static IQueryable<DeliveryNeighborhood> WhereNotAliasSource(
        this IQueryable<DeliveryNeighborhood> query,
        ApplicationDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(dbContext);

        return query.Where(neighborhood => !dbContext.DeliveryNeighborhoodAliases.Any(alias =>
            alias.IsActive
            && alias.IsApproved
            && alias.CityId == neighborhood.CityId
            && alias.NormalizedAlias == neighborhood.NormalizedName
            && alias.CanonicalNeighborhoodId != neighborhood.Id));
    }
}
