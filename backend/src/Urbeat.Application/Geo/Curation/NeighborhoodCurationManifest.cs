using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Urbeat.Domain.Geo;

namespace Urbeat.Application.Geo.Curation;

public static class NeighborhoodCurationManifestSchema
{
    public const string Current = "urbeat.neighborhood.alias-curation-manifest/1";

    public const string Approvals = "urbeat.neighborhood.alias-approvals/1";
}

/// <summary>
/// Content-addressed record of a dry-run. The manifest is the only input the apply/rollback steps
/// accept: <see cref="ManifestHash"/> is derived from the decisions and the input digests, never
/// from timestamps or local paths, so re-running the dry-run over the same inputs reproduces the
/// same hash on any machine.
/// </summary>
public sealed class NeighborhoodCurationManifest
{
    public string Schema { get; init; } = NeighborhoodCurationManifestSchema.Current;

    public int PolicyVersion { get; init; } = NeighborhoodAliasCurationPolicy.Version;

    public string GeneratedAtUtc { get; init; } = string.Empty;

    /// <summary>Always true for artifacts produced by the read-only dry-run.</summary>
    public bool DryRun { get; init; } = true;

    public string ManifestHash { get; init; } = string.Empty;

    public NeighborhoodCurationManifestInputs Inputs { get; init; } = new();

    public NeighborhoodCurationManifestTotals Totals { get; init; } = new();

    public IReadOnlyList<string> PolicyRules { get; init; } = NeighborhoodAliasCurationPolicy.Rules;

    public IReadOnlyList<NeighborhoodCurationDecision> Decisions { get; init; } = [];

    /// <summary>
    /// Explicit, auditable repairs for records that cannot be curated (missing city scope or test
    /// data). A repair never deletes a row.
    /// </summary>
    public IReadOnlyList<NeighborhoodCurationRepair> Repairs { get; init; } = [];

    public IReadOnlyList<NeighborhoodCurationDecision> ApplyableDecisions
        => Decisions.Where(decision => decision.Applyable).ToList();
}

public sealed class NeighborhoodCurationManifestInputs
{
    /// <summary>Informational path. Never part of the hash.</summary>
    public string QueuePath { get; init; } = string.Empty;

    public string QueueSha256 { get; init; } = string.Empty;

    public string? QueueSchema { get; init; }

    /// <summary>Informational path. Never part of the hash.</summary>
    public string? ApprovalsPath { get; init; }

    public string? ApprovalsSha256 { get; init; }

    /// <summary>Informational path of the explicit decisions file. Never part of the hash.</summary>
    public string? DecisionsPath { get; init; }

    public string? DecisionsSha256 { get; init; }
}

public sealed class NeighborhoodCurationManifestTotals
{
    public int Groups { get; init; }

    public int AutoApprove { get; init; }

    public int HumanReview { get; init; }

    public int ApprovedByReview { get; init; }

    public int Blocked { get; init; }

    public int KeepSeparate { get; init; }

    public int Applyable { get; init; }

    public int ApplyableAliases { get; init; }

    public int Repairs { get; init; }
}

public sealed record NeighborhoodCurationDecision
{
    public string GroupKey { get; init; } = string.Empty;

    public int PriorityRank { get; init; }

    public string Uf { get; init; } = string.Empty;

    public string Ibge { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string? CityId { get; init; }

    /// <summary>One of <see cref="NeighborhoodCurationOutcomeCodes"/>.</summary>
    public string Decision { get; init; } = NeighborhoodCurationOutcomeCodes.Blocked;

    public bool Applyable { get; init; }

    public IReadOnlyList<NeighborhoodCurationReason> Reasons { get; init; } = [];

    public string? CanonicalNeighborhoodId { get; init; }

    public string CanonicalName { get; init; } = string.Empty;

    public string RecommendedCanonicalName { get; init; } = string.Empty;

    /// <summary>Presentation-only uppercase label with accents preserved.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// When true, <see cref="RecommendedCanonicalName"/> becomes the stored canonical name. Only an
    /// accent/case-only change is accepted: the normalized value must stay identical.
    /// </summary>
    public bool UpdateCanonicalName { get; init; }

    public IReadOnlyList<NeighborhoodCurationAliasDecision> Aliases { get; init; } = [];

    /// <summary>Invariant decimal string, e.g. <c>0.011</c>.</summary>
    public string? DistanceMaxKm { get; init; }

    public string? DistanceMinKm { get; init; }

    public string? Evidence { get; init; }

    public NeighborhoodCurationReview? Review { get; init; }

    public NeighborhoodCurationOutcome Outcome
        => NeighborhoodCurationOutcomeCodes.ToOutcome(Decision);
}

public sealed record NeighborhoodCurationAliasDecision
{
    public string NeighborhoodId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string NormalizedName { get; init; } = string.Empty;

    /// <summary>
    /// Deterministic identifier derived from the manifest hash. Re-applying the same manifest
    /// therefore targets the same row, and rollback can recompute it without a journal table.
    /// </summary>
    public string AliasId { get; init; } = string.Empty;
}

public sealed class NeighborhoodCurationReview
{
    public string Reviewer { get; init; } = string.Empty;

    public string? ReviewerUserId { get; init; }

    public string ReviewedAtUtc { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// One explicit repair for a record outside the alias mechanism. <see cref="Kind"/> is either
/// <c>set_city</c> (assign an unambiguous city) or <c>deactivate</c> (test/duplicate data). A repair
/// never deletes a row and is idempotent.
/// </summary>
public sealed class NeighborhoodCurationRepair
{
    public string Kind { get; init; } = string.Empty;

    public string NeighborhoodId { get; init; } = string.Empty;

    public string? CityId { get; init; }

    public string? CityName { get; init; }

    public string? Uf { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Reviewer { get; init; } = string.Empty;

    public string ReviewedAtUtc { get; init; } = string.Empty;
}

/// <summary>Deterministic hashing and JSON round-tripping for curation manifests.</summary>
public static class NeighborhoodCurationManifestHasher
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    /// <summary>SHA-256 of the raw file bytes, lowercase hex.</summary>
    public static string ComputeFileHash(ReadOnlySpan<byte> content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    /// <summary>
    /// Hash of the decision semantics: policy version, input digests, group/decision identifiers,
    /// normalized names, reason codes and reviewer attribution. Human-readable messages, timestamps
    /// and local paths are deliberately excluded.
    /// </summary>
    public static string ComputeDecisionHash(
        int policyVersion,
        string queueSha256,
        string? approvalsSha256,
        IEnumerable<NeighborhoodCurationDecision> decisions,
        IEnumerable<NeighborhoodCurationRepair>? repairs = null)
    {
        var builder = new StringBuilder();
        builder.Append("urbeat-neighborhood-alias-curation\n");
        builder.Append("policy=").Append(policyVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("queue=").Append(queueSha256).Append('\n');
        builder.Append("approvals=").Append(approvalsSha256 ?? "-").Append('\n');

        foreach (var decision in decisions.OrderBy(x => x.GroupKey, StringComparer.Ordinal))
        {
            builder.Append("group=").Append(decision.GroupKey).Append('\n');
            builder.Append("decision=").Append(decision.Decision).Append('\n');
            builder.Append("city=").Append(NormalizeId(decision.CityId)).Append('\n');
            builder.Append("canonical=").Append(NormalizeId(decision.CanonicalNeighborhoodId)).Append('\n');
            builder.Append("canonical_name=").Append(NeighborhoodName.Normalize(decision.CanonicalName)).Append('\n');
            builder.Append("update=")
                .Append(decision.UpdateCanonicalName
                    ? NeighborhoodName.Normalize(decision.RecommendedCanonicalName)
                    : "-")
                .Append('\n');
            builder.Append("recommended=").Append(NeighborhoodName.Normalize(decision.RecommendedCanonicalName)).Append('\n');
            builder.Append("display=").Append(decision.DisplayName).Append('\n');
            builder.Append("distance=")
                .Append(decision.DistanceMaxKm ?? "-")
                .Append('/')
                .Append(decision.DistanceMinKm ?? "-")
                .Append('\n');
            builder.Append("reasons=")
                .Append(string.Join(',', decision.Reasons
                    .Select(reason => reason.Code)
                    .Order(StringComparer.Ordinal)))
                .Append('\n');
            builder.Append("aliases=")
                .Append(string.Join(';', decision.Aliases
                    .Select(alias => $"{NormalizeId(alias.NeighborhoodId)}:{alias.NormalizedName}")
                    .Order(StringComparer.Ordinal)))
                .Append('\n');

            if (decision.Review is null)
            {
                builder.Append("review=-\n");
            }
            else
            {
                builder.Append("review=")
                    .Append(decision.Review.Reviewer).Append('|')
                    .Append(NormalizeId(decision.Review.ReviewerUserId)).Append('|')
                    .Append(decision.Review.ReviewedAtUtc).Append('|')
                    .Append(decision.Review.Note)
                    .Append('\n');
            }
        }

        foreach (var repair in (repairs ?? []).OrderBy(x => x.NeighborhoodId, StringComparer.Ordinal))
        {
            builder.Append("repair=").Append(repair.Kind)
                .Append('|').Append(NormalizeId(repair.NeighborhoodId))
                .Append('|').Append(NormalizeId(repair.CityId))
                .Append('|').Append(repair.Reason)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// Deterministic alias row identifier. It is scoped by manifest so that different approved
    /// manifests never collide, and re-running the same manifest targets the same row.
    /// </summary>
    public static Guid AliasIdFor(string manifestHash, Guid cityId, string normalizedAlias)
    {
        var payload = $"urbeat-neighborhood-alias|{manifestHash}|{cityId:N}|{normalizedAlias}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static string FormatDistance(double? value)
    {
        return value is null ? string.Empty : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static bool HashMatches(NeighborhoodCurationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var computed = ComputeDecisionHash(
            manifest.PolicyVersion,
            manifest.Inputs.QueueSha256,
            manifest.Inputs.ApprovalsSha256,
            manifest.Decisions,
            manifest.Repairs);

        return string.Equals(computed, manifest.ManifestHash, StringComparison.OrdinalIgnoreCase);
    }

    public static string Serialize(NeighborhoodCurationManifest manifest)
        => JsonSerializer.Serialize(manifest, JsonOptions) + "\n";

    public static NeighborhoodCurationManifest Deserialize(string json)
        => JsonSerializer.Deserialize<NeighborhoodCurationManifest>(json, JsonOptions)
           ?? throw new InvalidOperationException("Manifesto de curadoria vazio ou invalido.");

    private static string NormalizeId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToLowerInvariant();
}
