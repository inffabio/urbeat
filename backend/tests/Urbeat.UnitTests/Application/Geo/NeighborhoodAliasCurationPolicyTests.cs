using FluentAssertions;
using Urbeat.Application.Geo.Curation;
using Urbeat.Domain.Geo;

namespace Urbeat.UnitTests.Application.Geo;

public sealed class NeighborhoodAliasCurationPolicyTests
{
    private static readonly Guid CanonicalId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AliasId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CityId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private static readonly NeighborhoodAliasCurationPolicy Policy = new();

    [Fact]
    public void Auto_approves_article_only_variation_against_official_cnefe()
    {
        var group = CreateGroup("JARDIM DAS FLORES", "Jardim Flores", recommended: "Jardim das Flores");

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.AutoApprove);
        assessment.IsApplyable.Should().BeTrue();
        assessment.Aliases.Should().ContainSingle();
        assessment.Aliases[0].NeighborhoodId.Should().Be(AliasId);
        assessment.Aliases[0].NormalizedName.Should().Be("jardim flores");
        assessment.DisplayName.Should().Be("JARDIM DAS FLORES");
        assessment.CanonicalNeighborhoodId.Should().Be(CanonicalId);
        assessment.CityId.Should().Be(CityId);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.StopwordOnlyVariation);
        assessment.Reasons.Should().OnlyContain(reason =>
            reason.Severity == NeighborhoodCurationSeverity.Info);
    }

    [Fact]
    public void Reviews_plural_divergence_instead_of_auto_approving()
    {
        var group = CreateGroup(
            "RESIDENCIAL TERRAS DO HIMALAIA",
            "Residencial Terra do Himaláia",
            recommended: "Residencial Terras do Himaláia");

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.HumanReview);
        assessment.IsApplyable.Should().BeFalse();
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.PluralDivergence
            && reason.Severity == NeighborhoodCurationSeverity.Review);
        assessment.DisplayName.Should().Be("RESIDENCIAL TERRAS DO HIMALÁIA");
    }

    [Fact]
    public void Reviews_token_order_divergence()
    {
        var group = CreateGroup("DISTRITO INDUSTRIAL II", "II Distrito Industrial");

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.HumanReview);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.TokenOrderDivergence);
        assessment.Reasons.Should().NotContain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.TokenDivergence);
    }

    [Fact]
    public void Blocks_when_the_canonical_does_not_come_from_cnefe()
    {
        var group = CreateGroup(
            "CHACARA POCOS DE CALDAS",
            "Chácaras Poços de Caldas",
            canonicalKind: GeographicProvenance.NeighborhoodKind.Legacy,
            cnefeSourceOk: false);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().ContainSingle(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.MissingOfficialCnefe);
        // The canonical must never be treated as its own alias, even without an official member.
        assessment.Aliases.Should().ContainSingle();
        assessment.Aliases[0].Name.Should().Be("Chácaras Poços de Caldas");
    }

    [Fact]
    public void Blocks_when_both_members_are_uppercase()
    {
        var group = CreateGroup("JARDIM DAS FLORES", "JARDIM FLORES", aliasIsUpper: true);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.UppercaseConflict);
    }

    [Fact]
    public void Blocks_when_a_store_delivery_area_is_linked()
    {
        var group = CreateGroup("JARDIM DAS FLORES", "Jardim Flores", storeAreaCount: 1);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.StoreDeliveryArea);
    }

    [Fact]
    public void Blocks_when_distance_is_above_two_kilometres()
    {
        var group = CreateGroup("JARDIM DAS FLORES", "Jardim Flores", distance: 2.4);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.DistanceAboveBlockLimit);
    }

    [Fact]
    public void Reviews_when_distance_is_above_the_automatic_limit()
    {
        var group = CreateGroup("JARDIM DAS FLORES", "Jardim Flores", distance: 0.9);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.HumanReview);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.DistanceAboveAutoLimit);
    }

    [Fact]
    public void Blocks_accent_only_difference_because_it_is_a_duplicate_row()
    {
        var group = CreateGroup("CHACARA DE INOA", "Chácara de Inoã", recommended: "Chácara de Inoã");

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.DuplicateNormalizedName);
    }

    [Fact]
    public void Blocks_indigenous_or_rural_markers()
    {
        var group = CreateGroup("ALDEIA SÃO JOSÉ", "Aldeia Sao Jose Velha");

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.IndigenousOrRuralMarker);
    }

    [Fact]
    public void Reviews_generic_single_token_names()
    {
        var group = CreateGroup("CENTRO", "Centro da Cidade");

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.HumanReview);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.GenericName);
    }

    [Fact]
    public void Blocks_group_with_more_than_two_members()
    {
        var group = CreateGroup(
            "JARDIM DAS FLORES",
            "Jardim Flores",
            extraMembers:
            [
                new NeighborhoodCurationQueueMember
                {
                    Id = Guid.Parse("44444444-4444-4444-8444-444444444444").ToString(),
                    Name = "Jd Flores",
                    Kind = GeographicProvenance.NeighborhoodKind.Legacy
                }
            ]);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.MemberCountNotTwo);
    }

    [Theory]
    [InlineData(null, "3550308")]
    [InlineData("S", "3550308")]
    [InlineData("SP", "355")]
    [InlineData("SP", null)]
    public void Blocks_when_city_scope_is_incomplete(string? uf, string? ibge)
    {
        var group = CreateGroup("JARDIM DAS FLORES", "Jardim Flores", uf: uf, ibge: ibge);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
    }

    [Fact]
    public void Blocks_when_city_id_is_invalid()
    {
        var group = CreateGroup("JARDIM DAS FLORES", "Jardim Flores", cityId: "cidade-sem-guid");

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.MissingCityId);
    }

    [Fact]
    public void Blocks_when_canonical_id_points_to_another_member()
    {
        var group = CreateGroup(
            "JARDIM DAS FLORES",
            "Jardim Flores",
            canonicalId: AliasId.ToString(),
            canonicalMemberId: CanonicalId.ToString());

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.CanonicalMismatch);
    }

    [Fact]
    public void Blocks_manual_members()
    {
        var group = CreateGroup(
            "JARDIM DAS FLORES",
            "Jardim Flores",
            aliasKind: GeographicProvenance.NeighborhoodKind.Manual);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.ManualMember);
    }

    [Fact]
    public void Reviews_when_the_canonical_loses_accents()
    {
        var group = CreateGroup(
            "CHACARA VIRGINIA",
            "Chácaras Virgínia",
            recommended: "Chácara Virgínia",
            accentInferiority: true);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.HumanReview);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.CanonicalMissingAccents);
        assessment.DisplayName.Should().Be("CHÁCARA VIRGÍNIA");
    }

    [Fact]
    public void Blocks_when_the_recommendation_changes_letters()
    {
        var group = CreateGroup(
            "JARDIM DAS FLORES",
            "Jardim Flores",
            recommended: "Jardim Das Floradas");

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.CanonicalRecommendationMismatch);
    }

    [Fact]
    public void Blocks_when_distance_is_missing()
    {
        var group = CreateGroup("JARDIM DAS FLORES", "Jardim Flores", distance: null);

        var assessment = Policy.Assess(group);

        assessment.Outcome.Should().Be(NeighborhoodCurationOutcome.Blocked);
        assessment.Reasons.Should().Contain(reason =>
            reason.Code == NeighborhoodCurationReasonCodes.MissingDistance);
    }

    private static NeighborhoodCurationQueueGroup CreateGroup(
        string canonicalName,
        string? aliasName,
        string recommended = "",
        string? cityId = null,
        string? uf = "SP",
        string? ibge = "3550308",
        double? distance = 0.2,
        string canonicalKind = GeographicProvenance.NeighborhoodKind.OfficialCnefe,
        string aliasKind = GeographicProvenance.NeighborhoodKind.Legacy,
        bool canonicalIsUpper = true,
        bool aliasIsUpper = false,
        bool cnefeSourceOk = true,
        bool accentInferiority = false,
        int storeAreaCount = 0,
        string? canonicalId = null,
        string? canonicalMemberId = null,
        string? aliasId = null,
        IReadOnlyList<NeighborhoodCurationQueueMember>? extraMembers = null,
        string grupo = "G000001")
    {
        var members = new List<NeighborhoodCurationQueueMember>
        {
            new()
            {
                Id = canonicalMemberId ?? canonicalId ?? CanonicalId.ToString(),
                Name = canonicalName,
                Kind = canonicalKind,
                Source = "cnefe",
                IsUpper = canonicalIsUpper
            }
        };

        if (aliasName is not null)
        {
            members.Add(new NeighborhoodCurationQueueMember
            {
                Id = aliasId ?? AliasId.ToString(),
                Name = aliasName,
                Kind = aliasKind,
                Source = "legacy",
                IsUpper = aliasIsUpper
            });
        }

        if (extraMembers is not null)
        {
            members.AddRange(extraMembers);
        }

        return new NeighborhoodCurationQueueGroup
        {
            PriorityRank = 1,
            Grupo = grupo,
            Uf = uf,
            Ibge = ibge,
            Cidade = "Cidade Teste",
            CityId = cityId ?? CityId.ToString(),
            CanonicalId = canonicalId ?? CanonicalId.ToString(),
            CanonicalName = canonicalName,
            CanonicalKind = canonicalKind,
            CanonicalSource = "cnefe",
            Distance = distance,
            DistanceMinKm = distance,
            StoreAreaCount = storeAreaCount,
            CnefeSourceOk = cnefeSourceOk,
            AccentInferiority = accentInferiority,
            AccentRestorationSafe = true,
            RecommendedCanonicalName = recommended.Length == 0 ? canonicalName : recommended,
            DisplayNameUppercase = canonicalName,
            Members = members,
            Flags = []
        };
    }
}
