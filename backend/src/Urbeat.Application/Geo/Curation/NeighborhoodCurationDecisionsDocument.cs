namespace Urbeat.Application.Geo.Curation;

public static class NeighborhoodCurationDecisionsSchema
{
    public const string Current = "urbeat.neighborhood.alias-decisions/1";
}

/// <summary>
/// Explicit national curation decisions. The policy still evaluates every group first; this file
/// only supplies the human decision for groups that the conservative policy cannot resolve alone
/// (uppercase conflicts, store-linked groups, groups outside the auto queue). It cannot invent
/// members: canonical and alias identifiers must belong to the queue group and city.
/// </summary>
public sealed class NeighborhoodCurationDecisionsDocument
{
    public string Schema { get; init; } = NeighborhoodCurationDecisionsSchema.Current;

    public string GeneratedUtc { get; init; } = string.Empty;

    public IReadOnlyList<NeighborhoodCurationExplicitDecision> Decisions { get; init; } = [];
}

public sealed class NeighborhoodCurationExplicitDecision
{
    /// <summary><c>merge</c> registers aliases; <c>keep_separate</c> writes nothing.</summary>
    public string Action { get; init; } = string.Empty;

    public string? GroupKey { get; init; }

    public string? CanonicalNeighborhoodId { get; init; }

    /// <summary>
    /// Desired stored canonical name. It may only differ from the current name by accents/case:
    /// the normalized value must stay identical.
    /// </summary>
    public string? CanonicalName { get; init; }

    public IReadOnlyList<NeighborhoodCurationExplicitAlias> Aliases { get; init; } = [];

    public string Reviewer { get; init; } = string.Empty;

    public string? ReviewerUserId { get; init; }

    public string? ReviewedAtUtc { get; init; }

    public string Note { get; init; } = string.Empty;

    public string? Evidence { get; init; }
}

public sealed class NeighborhoodCurationExplicitAlias
{
    public string? NeighborhoodId { get; init; }

    /// <summary>Optional assertion; when present it must match the persisted source name.</summary>
    public string? Name { get; init; }
}

/// <summary>Explicit repairs for records that cannot participate in alias curation.</summary>
public sealed class NeighborhoodCurationRepairsDocument
{
    public string Schema { get; init; } = "urbeat.neighborhood.alias-repairs/1";

    public IReadOnlyList<NeighborhoodCurationExplicitRepair> Repairs { get; init; } = [];
}

public sealed class NeighborhoodCurationExplicitRepair
{
    /// <summary><c>set_city</c> or <c>deactivate</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    public string? NeighborhoodId { get; init; }

    public string? CityId { get; init; }

    public string? CityName { get; init; }

    public string? Uf { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Reviewer { get; init; } = string.Empty;

    public string? ReviewedAtUtc { get; init; }
}
