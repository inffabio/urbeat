namespace Urbeat.Application.Geo.Curation;

/// <summary>
/// Reviewer decisions for groups classified as <see cref="NeighborhoodCurationOutcome.HumanReview"/>.
/// The file never overrides a blocked group and never changes the recommended spelling: it can only
/// confirm the exact values the dry-run proposed.
/// </summary>
public sealed class NeighborhoodCurationApprovalsDocument
{
    public string Schema { get; init; } = NeighborhoodCurationManifestSchema.Approvals;

    public IReadOnlyList<NeighborhoodCurationApproval> Approvals { get; init; } = [];
}

public sealed class NeighborhoodCurationApproval
{
    public string? GroupKey { get; init; }

    public string? Reviewer { get; init; }

    public string? ReviewerUserId { get; init; }

    /// <summary>ISO-8601 UTC instant, e.g. <c>2026-11-16T12:00:00Z</c>.</summary>
    public string? ReviewedAtUtc { get; init; }

    public string? Note { get; init; }

    public string? ConfirmedCanonicalName { get; init; }

    public string? ConfirmedDisplayName { get; init; }

    public IReadOnlyList<string> ConfirmedAliasNames { get; init; } = [];
}
