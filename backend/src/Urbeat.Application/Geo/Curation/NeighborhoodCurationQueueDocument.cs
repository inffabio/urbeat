namespace Urbeat.Application.Geo.Curation;

/// <summary>
/// Read-only view of the national uppercase approval queue produced by the neighborhood curation
/// pipeline (<c>urbeat.neighborhood.uppercase-approval-queue/1</c>). Deserialization ignores fields
/// that are not needed by the policy, so the queue producer can add evidence without breaking the
/// curator.
/// </summary>
public sealed class NeighborhoodCurationQueueDocument
{
    public string Schema { get; init; } = string.Empty;

    public string GeneratedUtc { get; init; } = string.Empty;

    public NeighborhoodCurationQueueScope? Scope { get; init; }

    public IReadOnlyList<NeighborhoodCurationQueueGroup> Groups { get; init; } = [];
}

public sealed class NeighborhoodCurationQueueScope
{
    public bool National { get; init; }

    public int PriorityGroups { get; init; }

    public string? Note { get; init; }
}

public sealed class NeighborhoodCurationQueueGroup
{
    public int PriorityRank { get; init; }

    public string? Grupo { get; init; }

    public string? Uf { get; init; }

    public string? Ibge { get; init; }

    public string? Cidade { get; init; }

    public string? CityId { get; init; }

    public string? CanonicalRecommendationStatus { get; init; }

    public string? CanonicalId { get; init; }

    public string? CanonicalName { get; init; }

    public string? CanonicalSource { get; init; }

    public string? CanonicalKind { get; init; }

    public double? Distance { get; init; }

    public double? DistanceMinKm { get; init; }

    public string? Confidence { get; init; }

    public int StoreAreaCount { get; init; }

    public int StoreAreaActiveCount { get; init; }

    public bool? CnefeSourceOk { get; init; }

    public bool? AccentInferiority { get; init; }

    public bool? AccentRestorationSafe { get; init; }

    public string? RecommendedCanonicalName { get; init; }

    public string? DisplayNameUppercase { get; init; }

    public string? OrthographicEvidenceName { get; init; }

    public string? Evidence { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<NeighborhoodCurationQueueMember> Members { get; init; } = [];

    public IReadOnlyList<string> Flags { get; init; } = [];

    public IReadOnlyList<string> ReviewReasons { get; init; } = [];
}

public sealed class NeighborhoodCurationQueueMember
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? Kind { get; init; }

    public string? Source { get; init; }

    public bool IsUpper { get; init; }

    public int Diacritics { get; init; }

    public int StoreDeliveryAreas { get; init; }
}
