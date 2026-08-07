namespace Urbeat.Domain.Entities;

public sealed class City : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Uf { get; set; } = string.Empty;

    public string? IbgeCode { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? OsmId { get; set; }

    public string? OsmAreaId { get; set; }
}
