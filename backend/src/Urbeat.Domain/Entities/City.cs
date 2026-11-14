namespace Urbeat.Domain.Entities;

public sealed class City : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Uf { get; set; } = string.Empty;

    public string? IbgeCode { get; set; }

    /// <summary>Canonical, accent-insensitive, punctuation-free form of <see cref="Name"/>.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Provenance of <see cref="Latitude"/>/<see cref="Longitude"/> (never inferred silently).</summary>
    public string? CoordinateMethod { get; set; }

    /// <summary>Declared source of the city row, e.g. <c>ibge_localidades</c>, <c>openstreetmap</c>.</summary>
    public string Source { get; set; } = "openstreetmap";

    public string? SourceVersion { get; set; }

    public string? OsmId { get; set; }

    public string? OsmAreaId { get; set; }
}
