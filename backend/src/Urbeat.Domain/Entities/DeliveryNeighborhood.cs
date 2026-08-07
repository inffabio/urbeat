namespace Urbeat.Domain.Entities;

public sealed class DeliveryNeighborhood : BaseEntity
{
    public Guid? CityId { get; set; }
    public City? CityEntity { get; set; }

    public string City { get; set; } = string.Empty;

    public string Neighborhood { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string? OsmId { get; set; }

    public string? OsmType { get; set; }

    public string? PlaceType { get; set; }

    public string? Boundary { get; set; }

    public string? AdminLevel { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Source { get; set; }

    public bool IsActive { get; set; } = true;
}
