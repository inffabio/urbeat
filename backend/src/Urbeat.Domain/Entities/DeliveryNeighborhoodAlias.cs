namespace Urbeat.Domain.Entities;

/// <summary>
/// An explicitly approved alternate name for a canonical delivery neighborhood.
/// This table is intentionally empty until a curator reviews and approves a decision.
/// </summary>
public sealed class DeliveryNeighborhoodAlias : BaseEntity
{
    public DeliveryNeighborhoodAlias()
    {
    }

    /// <summary>
    /// Creates an alias with a caller-supplied identifier. Curation tooling derives the id from the
    /// approved manifest so re-applying the same manifest is idempotent.
    /// </summary>
    public DeliveryNeighborhoodAlias(Guid id)
        : base(id)
    {
    }

    public Guid CityId { get; set; }
    public City? CityEntity { get; set; }

    public Guid CanonicalNeighborhoodId { get; set; }
    public DeliveryNeighborhood? CanonicalNeighborhood { get; set; }

    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public bool IsApproved { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string? ApprovalNotes { get; set; }
}
