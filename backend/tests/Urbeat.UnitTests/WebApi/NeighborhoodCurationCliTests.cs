using FluentAssertions;
using Urbeat.Application.Geo.Curation;
using Urbeat.Domain.Geo;
using Urbeat.WebApi.Infrastructure;

namespace Urbeat.UnitTests.WebApi;

public sealed class NeighborhoodCurationCliTests
{
    [Fact]
    public void Write_commands_require_the_exact_manifest_hash()
    {
        var manifest = BuildManifest();

        var missing = () => NeighborhoodCurationCli.ValidateConfirmation(manifest, null, "apply");
        var wrong = () => NeighborhoodCurationCli.ValidateConfirmation(
            manifest,
            new string('0', 64),
            "rollback");
        var confirmed = () => NeighborhoodCurationCli.ValidateConfirmation(
            manifest,
            manifest.ManifestHash.ToUpperInvariant(),
            "apply");

        missing.Should().Throw<InvalidOperationException>().WithMessage("*confirm-hash*");
        wrong.Should().Throw<InvalidOperationException>().WithMessage("*confirm-hash*");
        confirmed.Should().NotThrow();
    }

    [Fact]
    public void Confirmation_is_refused_when_the_manifest_content_was_modified()
    {
        var original = BuildManifest();
        var tampered = new NeighborhoodCurationManifest
        {
            Schema = original.Schema,
            PolicyVersion = original.PolicyVersion,
            GeneratedAtUtc = original.GeneratedAtUtc,
            DryRun = original.DryRun,
            ManifestHash = original.ManifestHash,
            Inputs = original.Inputs,
            Totals = original.Totals,
            Decisions = original.Decisions
                .Select(decision => decision with { DisplayName = decision.DisplayName + " X" })
                .ToList()
        };

        var act = () => NeighborhoodCurationCli.ValidateConfirmation(
            tampered,
            tampered.ManifestHash,
            "apply");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Hash*");
    }

    private static NeighborhoodCurationManifest BuildManifest()
    {
        var canonicalId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var aliasId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var cityId = Guid.Parse("33333333-3333-4333-8333-333333333333");

        var queue = new NeighborhoodCurationQueueDocument
        {
            Schema = "urbeat.neighborhood.uppercase-approval-queue/1",
            Groups =
            [
                new NeighborhoodCurationQueueGroup
                {
                    PriorityRank = 1,
                    Grupo = "G000001",
                    Uf = "SP",
                    Ibge = "3550308",
                    Cidade = "Cidade Teste",
                    CityId = cityId.ToString(),
                    CanonicalId = canonicalId.ToString(),
                    CanonicalName = "JARDIM DAS FLORES",
                    CanonicalKind = GeographicProvenance.NeighborhoodKind.OfficialCnefe,
                    Distance = 0.2,
                    DistanceMinKm = 0.2,
                    CnefeSourceOk = true,
                    RecommendedCanonicalName = "JARDIM DAS FLORES",
                    Members =
                    [
                        new NeighborhoodCurationQueueMember
                        {
                            Id = canonicalId.ToString(),
                            Name = "JARDIM DAS FLORES",
                            Kind = GeographicProvenance.NeighborhoodKind.OfficialCnefe,
                            IsUpper = true
                        },
                        new NeighborhoodCurationQueueMember
                        {
                            Id = aliasId.ToString(),
                            Name = "Jardim Flores",
                            Kind = GeographicProvenance.NeighborhoodKind.Legacy
                        }
                    ]
                }
            ]
        };

        return new NeighborhoodAliasCurator().BuildManifest(
            queue,
            "queue.json",
            new string('a', 64));
    }
}
