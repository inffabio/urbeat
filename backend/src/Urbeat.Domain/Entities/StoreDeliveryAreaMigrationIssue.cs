namespace Urbeat.Domain.Entities;

/// <summary>
/// Auditable record for a <see cref="StoreDeliveryArea"/> row that could not be linked to a
/// <see cref="DeliveryNeighborhood"/> during the identifier backfill. The original row is never
/// deleted; it is reported here for manual curation.
/// </summary>
public sealed class StoreDeliveryAreaMigrationIssue : BaseEntity
{
    public Guid StoreDeliveryAreaId { get; set; }

    public Guid StoreId { get; set; }

    public string Neighborhood { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? Uf { get; set; }

    /// <summary><c>unresolved_neighborhood</c>, <c>ambiguous_neighborhood</c>, <c>missing_store_city</c>, ...</summary>
    public string Reason { get; set; } = "unresolved_neighborhood";

    public string? Details { get; set; }

    public string SourceVersion { get; set; } = string.Empty;

    public static StoreDeliveryAreaMigrationIssue UnresolvedNeighborhood(
        StoreDeliveryArea area,
        string city,
        string? uf,
        string sourceVersion,
        string? details = null)
    {
        return new StoreDeliveryAreaMigrationIssue
        {
            StoreDeliveryAreaId = area.Id,
            StoreId = area.StoreId,
            Neighborhood = area.Neighborhood,
            NormalizedName = Geo.NeighborhoodName.Normalize(area.Neighborhood),
            City = city,
            Uf = uf,
            Reason = "unresolved_neighborhood",
            Details = details,
            SourceVersion = sourceVersion
        };
    }
}
