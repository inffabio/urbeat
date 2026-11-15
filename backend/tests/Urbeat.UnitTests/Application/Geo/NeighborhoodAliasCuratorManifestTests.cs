using FluentAssertions;
using Urbeat.Application.Geo.Curation;
using Urbeat.Domain.Geo;

namespace Urbeat.UnitTests.Application.Geo;

public sealed class NeighborhoodAliasCuratorManifestTests
{
    private static readonly Guid CanonicalId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AliasId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CityId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private const string QueueHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly NeighborhoodAliasCurator _curator = new();

    [Fact]
    public void Manifest_hash_is_content_addressed_and_reproducible()
    {
        var group = AutoApproveGroup("G000001");
        var queue = CreateQueue(group);

        var first = _curator.BuildManifest(queue, "queue.json", QueueHash, generatedAtUtc: At("2026-11-15T10:00:00Z"));
        var second = _curator.BuildManifest(queue, "outra/pasta/queue.json", QueueHash, generatedAtUtc: At("2026-11-16T23:59:59Z"));

        first.ManifestHash.Should().Be(second.ManifestHash);
        first.GeneratedAtUtc.Should().NotBe(second.GeneratedAtUtc);
        first.DryRun.Should().BeTrue();
        first.Totals.AutoApprove.Should().Be(1);
        first.Totals.Applyable.Should().Be(1);
        first.Totals.ApplyableAliases.Should().Be(1);
        first.Decisions.Single().Aliases.Single().AliasId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Manifest_changes_hash_when_a_decision_changes()
    {
        var queue = CreateQueue(AutoApproveGroup("G000001"));

        var baseline = _curator.BuildManifest(queue, "queue.json", QueueHash);
        var otherQueue = CreateQueue(AutoApproveGroup("G000001", aliasName: "Jardim De Flores"));

        var other = _curator.BuildManifest(otherQueue, "queue.json", QueueHash);

        other.ManifestHash.Should().NotBe(baseline.ManifestHash);
    }

    [Fact]
    public void Manifest_roundtrips_through_json_without_losing_the_hash()
    {
        var manifest = _curator.BuildManifest(CreateQueue(AutoApproveGroup("G000001")), "queue.json", QueueHash);

        var json = NeighborhoodCurationManifestHasher.Serialize(manifest);
        var restored = NeighborhoodCurationManifestHasher.Deserialize(json);

        NeighborhoodCurationManifestHasher.HashMatches(restored).Should().BeTrue();
        restored.ManifestHash.Should().Be(manifest.ManifestHash);
        restored.Decisions.Single().DisplayName.Should().Be("JARDIM DAS FLORES");
        restored.Decisions.Single().Reasons.Should().Contain(reason =>
            reason.Severity == NeighborhoodCurationSeverity.Info);
    }

    [Fact]
    public void Approved_review_promotes_the_group_and_changes_the_hash()
    {
        var group = ReviewGroup("G000002");
        var queue = CreateQueue(group);
        var baseline = _curator.BuildManifest(queue, "queue.json", QueueHash);

        var approvals = new NeighborhoodCurationApprovalsDocument
        {
            Approvals =
            [
                new NeighborhoodCurationApproval
                {
                    GroupKey = "G000002",
                    Reviewer = "arquiteto",
                    ReviewerUserId = "55555555-5555-4555-8555-555555555555",
                    ReviewedAtUtc = "2026-11-16T12:00:00Z",
                    Note = "Grafia confirmada no CNEFE e no cadastro municipal.",
                    ConfirmedCanonicalName = "Residencial Terras do Himaláia",
                    ConfirmedDisplayName = "RESIDENCIAL TERRAS DO HIMALÁIA",
                    ConfirmedAliasNames = ["Residencial Terra do Himaláia"]
                }
            ]
        };

        var manifest = _curator.BuildManifest(
            queue,
            "queue.json",
            QueueHash,
            approvals,
            "approvals.json",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        var decision = manifest.Decisions.Single();
        decision.Decision.Should().Be(NeighborhoodCurationOutcomeCodes.ApprovedByReview);
        decision.Applyable.Should().BeTrue();
        decision.Review!.Reviewer.Should().Be("arquiteto");
        decision.Review.ReviewedAtUtc.Should().Be("2026-11-16T12:00:00Z");
        decision.Aliases.Single().AliasId.Should().NotBeNullOrWhiteSpace();
        manifest.Totals.ApprovedByReview.Should().Be(1);
        manifest.ManifestHash.Should().NotBe(baseline.ManifestHash);
    }

    [Fact]
    public void Approval_for_a_blocked_group_is_rejected()
    {
        var group = AutoApproveGroup("G000003", canonicalKind: GeographicProvenance.NeighborhoodKind.Legacy, cnefeSourceOk: false);
        var approvals = Approval("G000003");

        var act = () => _curator.BuildManifest(CreateQueue(group), "queue.json", QueueHash, approvals, "approvals.json", "b");

        act.Should().Throw<InvalidOperationException>().WithMessage("*BLOCKED*");
    }

    [Fact]
    public void Approval_for_an_auto_approved_group_is_rejected()
    {
        var approvals = Approval("G000004");

        var act = () => _curator.BuildManifest(CreateQueue(AutoApproveGroup("G000004")), "queue.json", QueueHash, approvals, "a", "b");

        act.Should().Throw<InvalidOperationException>().WithMessage("*AUTO_APPROVE*");
    }

    [Fact]
    public void Approval_must_confirm_the_exact_recommended_spelling()
    {
        var approvals = Approval("G000005", canonicalName: "Residencial Terras do Himalaia");

        var act = () => _curator.BuildManifest(CreateQueue(ReviewGroup("G000005")), "queue.json", QueueHash, approvals, "a", "b");

        act.Should().Throw<InvalidOperationException>().WithMessage("*confirmed_canonical_name*");
    }

    [Fact]
    public void Approval_requires_an_explicit_utc_instant()
    {
        var approvals = Approval("G000006", reviewedAtUtc: "2026-11-16T12:00:00-03:00");

        var act = () => _curator.BuildManifest(CreateQueue(ReviewGroup("G000006")), "queue.json", QueueHash, approvals, "a", "b");

        act.Should().Throw<InvalidOperationException>().WithMessage("*UTC*");
    }

    [Fact]
    public void Approval_for_an_unknown_group_is_rejected()
    {
        var approvals = Approval("G999999");

        var act = () => _curator.BuildManifest(CreateQueue(ReviewGroup("G000007")), "queue.json", QueueHash, approvals, "a", "b");

        act.Should().Throw<InvalidOperationException>().WithMessage("*nao existe*");
    }

    [Fact]
    public void Conflicting_alias_claims_across_groups_are_blocked()
    {
        var first = AutoApproveGroup(
            "G000010",
            canonicalName: "JARDIM DAS FLORES",
            aliasName: "Jardim Flores",
            canonicalId: CanonicalId);
        var second = AutoApproveGroup(
            "G000011",
            canonicalName: "JARDIM DE FLORES",
            aliasName: "Jardim Flores",
            canonicalId: Guid.Parse("44444444-4444-4444-8444-444444444444"));

        var manifest = _curator.BuildManifest(CreateQueue(first, second), "queue.json", QueueHash);

        manifest.Decisions.Should().OnlyContain(decision =>
            decision.Decision == NeighborhoodCurationOutcomeCodes.Blocked);
        manifest.Decisions.Should().OnlyContain(decision => decision.Reasons.Any(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.AliasConflictAcrossGroups));
        manifest.Totals.Applyable.Should().Be(0);
    }

    [Fact]
    public void Explicit_merge_overrides_a_blocked_group_and_restores_accents()
    {
        var group = AutoApproveGroup(
            "G000020",
            canonicalName: "JARDIM DAS FLORES",
            canonicalKind: GeographicProvenance.NeighborhoodKind.Legacy,
            cnefeSourceOk: false);
        var decisions = new NeighborhoodCurationDecisionsDocument
        {
            Decisions =
            [
                new NeighborhoodCurationExplicitDecision
                {
                    GroupKey = "G000020",
                    Action = "merge",
                    CanonicalNeighborhoodId = CanonicalId.ToString(),
                    CanonicalName = "JARDIM DAS FLÔRES",
                    Reviewer = "curadoria-nacional",
                    ReviewedAtUtc = "2026-11-15T12:00:00Z",
                    Note = "Grafia confirmada; somente acentos foram corrigidos.",
                    Evidence = "explicit-test"
                }
            ]
        };

        var manifest = _curator.BuildManifest(
            CreateQueue(group),
            "queue.json",
            QueueHash,
            explicitDecisions: decisions,
            decisionsPath: "decisions.json",
            decisionsSha256: "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");

        var decision = manifest.Decisions.Single();
        decision.Decision.Should().Be(NeighborhoodCurationOutcomeCodes.ApprovedByReview);
        decision.Applyable.Should().BeTrue();
        decision.UpdateCanonicalName.Should().BeTrue();
        decision.RecommendedCanonicalName.Should().Be("JARDIM DAS FLÔRES");
        decision.DisplayName.Should().Be("JARDIM DAS FLÔRES");
        decision.Review!.Reviewer.Should().Be("curadoria-nacional");
        manifest.Totals.ApplyableAliases.Should().Be(1);
    }

    [Fact]
    public void Explicit_keep_separate_records_the_decision_without_aliases()
    {
        var group = AutoApproveGroup("G000021");
        var decisions = new NeighborhoodCurationDecisionsDocument
        {
            Decisions =
            [
                new NeighborhoodCurationExplicitDecision
                {
                    GroupKey = "G000021",
                    Action = "keep_separate",
                    CanonicalNeighborhoodId = CanonicalId.ToString(),
                    Reviewer = "curadoria-nacional",
                    ReviewedAtUtc = "2026-11-15T12:00:00Z",
                    Note = "Evidencia insuficiente para unificar."
                }
            ]
        };

        var manifest = _curator.BuildManifest(CreateQueue(group), "queue.json", QueueHash, explicitDecisions: decisions);

        var decision = manifest.Decisions.Single();
        decision.Decision.Should().Be(NeighborhoodCurationOutcomeCodes.KeepSeparate);
        decision.Applyable.Should().BeFalse();
        decision.Aliases.Should().BeEmpty();
        manifest.Totals.KeepSeparate.Should().Be(1);
    }

    [Fact]
    public void Repairs_are_part_of_the_manifest_hash()
    {
        var queue = CreateQueue(AutoApproveGroup("G000022"));
        var repairs = new NeighborhoodCurationRepairsDocument
        {
            Repairs =
            [
                new NeighborhoodCurationExplicitRepair
                {
                    Kind = "deactivate",
                    NeighborhoodId = "99999999-9999-4999-8999-999999999999",
                    Reason = "registro de teste",
                    Reviewer = "curadoria-nacional",
                    ReviewedAtUtc = "2026-11-15T12:00:00Z"
                }
            ]
        };

        var baseline = _curator.BuildManifest(queue, "queue.json", QueueHash);
        var withRepair = _curator.BuildManifest(queue, "queue.json", QueueHash, repairsDocument: repairs);

        withRepair.Repairs.Should().ContainSingle();
        withRepair.Totals.Repairs.Should().Be(1);
        withRepair.ManifestHash.Should().NotBe(baseline.ManifestHash);
    }

    [Fact]
    public void Explicit_decision_for_an_unknown_group_is_rejected()
    {
        var decisions = new NeighborhoodCurationDecisionsDocument
        {
            Decisions =
            [
                new NeighborhoodCurationExplicitDecision
                {
                    GroupKey = "G999999",
                    Action = "merge",
                    CanonicalNeighborhoodId = CanonicalId.ToString(),
                    Reviewer = "curadoria-nacional",
                    ReviewedAtUtc = "2026-11-15T12:00:00Z",
                    Note = "x"
                }
            ]
        };

        var act = () => _curator.BuildManifest(
            CreateQueue(AutoApproveGroup("G000023")),
            "queue.json",
            QueueHash,
            explicitDecisions: decisions);

        act.Should().Throw<InvalidOperationException>().WithMessage("*nao existe*");
    }

    [Fact]
    public void Alias_id_is_deterministic_and_scoped_by_manifest()
    {
        var first = NeighborhoodCurationManifestHasher.AliasIdFor("hash-a", CityId, "jardim flores");
        var second = NeighborhoodCurationManifestHasher.AliasIdFor("hash-a", CityId, "jardim flores");
        var otherManifest = NeighborhoodCurationManifestHasher.AliasIdFor("hash-b", CityId, "jardim flores");
        var otherCity = NeighborhoodCurationManifestHasher.AliasIdFor("hash-a", Guid.NewGuid(), "jardim flores");

        first.Should().Be(second);
        first.Should().NotBe(otherManifest);
        first.Should().NotBe(otherCity);
    }

    private static NeighborhoodCurationApprovalsDocument Approval(
        string groupKey,
        string canonicalName = "Residencial Terras do Himaláia",
        string? reviewedAtUtc = "2026-11-16T12:00:00Z")
    {
        return new NeighborhoodCurationApprovalsDocument
        {
            Approvals =
            [
                new NeighborhoodCurationApproval
                {
                    GroupKey = groupKey,
                    Reviewer = "arquiteto",
                    ReviewedAtUtc = reviewedAtUtc,
                    Note = "Revisado manualmente com evidencia cadastral.",
                    ConfirmedCanonicalName = canonicalName,
                    ConfirmedDisplayName = "RESIDENCIAL TERRAS DO HIMALÁIA",
                    ConfirmedAliasNames = ["Residencial Terra do Himaláia"]
                }
            ]
        };
    }

    private static NeighborhoodCurationQueueDocument CreateQueue(params NeighborhoodCurationQueueGroup[] groups)
    {
        return new NeighborhoodCurationQueueDocument
        {
            Schema = "urbeat.neighborhood.uppercase-approval-queue/1",
            GeneratedUtc = "2026-11-15T05:40:22Z",
            Scope = new NeighborhoodCurationQueueScope { National = true, PriorityGroups = groups.Length },
            Groups = groups
        };
    }

    private static NeighborhoodCurationQueueGroup AutoApproveGroup(
        string grupo,
        string canonicalName = "JARDIM DAS FLORES",
        string aliasName = "Jardim Flores",
        string canonicalKind = GeographicProvenance.NeighborhoodKind.OfficialCnefe,
        bool cnefeSourceOk = true,
        Guid? canonicalId = null)
    {
        return Group(grupo, canonicalName, aliasName, canonicalName, canonicalKind, cnefeSourceOk, accentInferiority: false, canonicalId: canonicalId);
    }

    private static NeighborhoodCurationQueueGroup ReviewGroup(string grupo)
    {
        return Group(
            grupo,
            "RESIDENCIAL TERRAS DO HIMALAIA",
            "Residencial Terra do Himaláia",
            "Residencial Terras do Himaláia",
            GeographicProvenance.NeighborhoodKind.OfficialCnefe,
            cnefeSourceOk: true,
            accentInferiority: true);
    }

    private static NeighborhoodCurationQueueGroup Group(
        string grupo,
        string canonicalName,
        string aliasName,
        string recommended,
        string canonicalKind,
        bool cnefeSourceOk,
        bool accentInferiority,
        Guid? canonicalId = null)
    {
        var resolvedCanonicalId = canonicalId ?? CanonicalId;
        return new NeighborhoodCurationQueueGroup
        {
            PriorityRank = 1,
            Grupo = grupo,
            Uf = "SP",
            Ibge = "3550308",
            Cidade = "Cidade Teste",
            CityId = CityId.ToString(),
            CanonicalId = resolvedCanonicalId.ToString(),
            CanonicalName = canonicalName,
            CanonicalKind = canonicalKind,
            CanonicalSource = "cnefe",
            Distance = 0.2,
            DistanceMinKm = 0.2,
            CnefeSourceOk = cnefeSourceOk,
            AccentInferiority = accentInferiority,
            AccentRestorationSafe = true,
            RecommendedCanonicalName = recommended,
            Members =
            [
                new NeighborhoodCurationQueueMember
                {
                    Id = resolvedCanonicalId.ToString(),
                    Name = canonicalName,
                    Kind = canonicalKind,
                    Source = "cnefe",
                    IsUpper = true
                },
                new NeighborhoodCurationQueueMember
                {
                    Id = AliasId.ToString(),
                    Name = aliasName,
                    Kind = GeographicProvenance.NeighborhoodKind.Legacy,
                    Source = "legacy",
                    IsUpper = false
                }
            ],
            Flags = []
        };
    }

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
