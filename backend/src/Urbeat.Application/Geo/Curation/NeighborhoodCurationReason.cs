namespace Urbeat.Application.Geo.Curation;

/// <summary>
/// Severity of a single curation finding. <see cref="Block"/> findings can never be overridden by
/// an approvals file; <see cref="Review"/> findings require an explicit human decision.
/// </summary>
public enum NeighborhoodCurationSeverity
{
    Info,
    Review,
    Block
}

public static class NeighborhoodCurationSeverityCodes
{
    public const string Info = "info";
    public const string Review = "review";
    public const string Block = "block";

    public static string ToCode(this NeighborhoodCurationSeverity severity)
    {
        return severity switch
        {
            NeighborhoodCurationSeverity.Review => Review,
            NeighborhoodCurationSeverity.Block => Block,
            _ => Info
        };
    }

    public static NeighborhoodCurationSeverity FromCode(string? code)
    {
        return code switch
        {
            Review => NeighborhoodCurationSeverity.Review,
            Block => NeighborhoodCurationSeverity.Block,
            _ => NeighborhoodCurationSeverity.Info
        };
    }
}

/// <summary>
/// One auditable finding about a curated group. Codes are stable identifiers used by the manifest
/// hash and by the human review report; messages are human-readable Portuguese copy.
/// </summary>
public sealed record NeighborhoodCurationReason(
    string Code,
    NeighborhoodCurationSeverity Severity,
    string Message)
{
    public static NeighborhoodCurationReason Info(string code, string message)
        => new(code, NeighborhoodCurationSeverity.Info, message);

    public static NeighborhoodCurationReason Review(string code, string message)
        => new(code, NeighborhoodCurationSeverity.Review, message);

    public static NeighborhoodCurationReason Block(string code, string message)
        => new(code, NeighborhoodCurationSeverity.Block, message);
}

/// <summary>Stable reason codes. Never rename without bumping the policy version.</summary>
public static class NeighborhoodCurationReasonCodes
{
    // Auto-approval evidence.
    public const string StopwordOnlyVariation = "stopword_only_variation";
    public const string SameCityConfirmed = "same_city_confirmed";
    public const string OfficialCnefeCanonical = "official_cnefe_canonical";
    public const string SingleUppercaseCanonical = "single_uppercase_canonical";
    public const string DistanceWithinAutoLimit = "distance_within_auto_limit";

    // Human review outcomes.
    public const string ExplicitCuratorDecision = "explicit_curator_decision";
    public const string KeptSeparateByDecision = "kept_separate_by_decision";
    public const string CanonicalNameAccentsRestored = "canonical_name_accents_restored";
    public const string StoreAreasRemapped = "store_areas_remapped";

    // Review-required findings.
    public const string PluralDivergence = "plural_divergence";
    public const string TokenOrderDivergence = "token_order_divergence";
    public const string TokenDivergence = "token_divergence";
    public const string CanonicalMissingAccents = "canonical_missing_accents";
    public const string DistanceAboveAutoLimit = "distance_above_auto_limit";
    public const string GenericName = "generic_name";

    // Blocking findings.
    public const string MemberCountNotTwo = "member_count_not_two";
    public const string MissingOfficialCnefe = "missing_official_cnefe";
    public const string MissingCnefeSourceFlag = "missing_cnefe_source_flag";
    public const string ManualMember = "manual_member";
    public const string StoreDeliveryArea = "store_delivery_area";
    public const string DistanceAboveBlockLimit = "distance_above_block_limit";
    public const string MissingDistance = "missing_distance";
    public const string UppercaseConflict = "uppercase_conflict";
    public const string MissingCityId = "missing_city_id";
    public const string MissingUf = "missing_uf";
    public const string InvalidIbge = "invalid_ibge";
    public const string InvalidIdentifier = "invalid_identifier";
    public const string CanonicalMismatch = "canonical_mismatch";
    public const string CanonicalRecommendationMismatch = "canonical_recommendation_mismatch";
    public const string DuplicateNormalizedName = "duplicate_normalized_name";
    public const string IndigenousOrRuralMarker = "indigenous_or_rural_marker";
    public const string InvalidNameLength = "invalid_name_length";
    public const string AliasTooLong = "alias_too_long";
    public const string MissingCanonicalName = "missing_canonical_name";
    public const string AliasConflictAcrossGroups = "alias_conflict_across_groups";
}
