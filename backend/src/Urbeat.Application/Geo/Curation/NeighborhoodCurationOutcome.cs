namespace Urbeat.Application.Geo.Curation;

/// <summary>
/// Final classification of a curated neighborhood group. The vocabulary is persisted in the
/// curation manifest, so the string codes are part of the contract.
/// </summary>
public enum NeighborhoodCurationOutcome
{
    /// <summary>Every conservative guard passed; the alias may be applied without human review.</summary>
    AutoApprove,

    /// <summary>The data is structurally sound but the spelling needs a human decision.</summary>
    HumanReview,

    /// <summary>A curator explicitly approved a <see cref="HumanReview"/> group in an approvals file.</summary>
    ApprovedByReview,

    /// <summary>The group cannot be aliased safely; the data must be fixed first.</summary>
    Blocked,

    /// <summary>
    /// A curator explicitly decided to keep every member as its own neighborhood. Nothing is
    /// written; the decision is recorded in the manifest for audit.
    /// </summary>
    KeepSeparate
}

public static class NeighborhoodCurationOutcomeCodes
{
    public const string AutoApprove = "AUTO_APPROVE";
    public const string HumanReview = "HUMAN_REVIEW";
    public const string ApprovedByReview = "APPROVED_BY_REVIEW";
    public const string Blocked = "BLOCKED";
    public const string KeepSeparate = "KEEP_SEPARATE";

    public static string ToCode(this NeighborhoodCurationOutcome outcome)
    {
        return outcome switch
        {
            NeighborhoodCurationOutcome.AutoApprove => AutoApprove,
            NeighborhoodCurationOutcome.HumanReview => HumanReview,
            NeighborhoodCurationOutcome.ApprovedByReview => ApprovedByReview,
            NeighborhoodCurationOutcome.KeepSeparate => KeepSeparate,
            _ => Blocked
        };
    }

    public static NeighborhoodCurationOutcome ToOutcome(string? code)
    {
        return code switch
        {
            AutoApprove => NeighborhoodCurationOutcome.AutoApprove,
            HumanReview => NeighborhoodCurationOutcome.HumanReview,
            ApprovedByReview => NeighborhoodCurationOutcome.ApprovedByReview,
            KeepSeparate => NeighborhoodCurationOutcome.KeepSeparate,
            _ => NeighborhoodCurationOutcome.Blocked
        };
    }

    /// <summary>Only auto-approved or explicitly reviewed decisions can be written by the apply step.</summary>
    public static bool IsApplyable(this NeighborhoodCurationOutcome outcome)
    {
        return outcome is NeighborhoodCurationOutcome.AutoApprove or NeighborhoodCurationOutcome.ApprovedByReview;
    }

    /// <summary>Higher wins when a group accumulates reasons of different severities.</summary>
    public static int Severity(this NeighborhoodCurationOutcome outcome)
    {
        return outcome switch
        {
            NeighborhoodCurationOutcome.Blocked => 2,
            NeighborhoodCurationOutcome.HumanReview or NeighborhoodCurationOutcome.KeepSeparate => 1,
            _ => 0
        };
    }
}
