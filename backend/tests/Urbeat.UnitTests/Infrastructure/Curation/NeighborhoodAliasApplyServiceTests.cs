using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Urbeat.Application.Geo.Curation;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Geo;
using Urbeat.Infrastructure.Curation;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Curation;

public sealed class NeighborhoodAliasApplyServiceTests
{
    private const string QueueHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public async Task Apply_inserts_the_alias_and_sets_the_display_name_without_renaming_rows()
    {
        var fixture = await CreateFixtureAsync();

        await using var db = fixture.Db;
        var result = await new NeighborhoodAliasApplyService(db).ApplyAsync(fixture.Manifest);

        result.AliasesInserted.Should().Be(1);
        result.DisplayNamesSet.Should().Be(1);
        result.AppliedAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var alias = await db.DeliveryNeighborhoodAliases.SingleAsync();
        alias.CityId.Should().Be(fixture.Canonical.CityId!.Value);
        alias.CanonicalNeighborhoodId.Should().Be(fixture.Canonical.Id);
        alias.Alias.Should().Be("Jardim Flores");
        alias.NormalizedAlias.Should().Be("jardim flores");
        alias.IsActive.Should().BeTrue();
        alias.IsApproved.Should().BeTrue();
        alias.ApprovedAtUtc.Should().NotBeNull();
        alias.ApprovedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        alias.ApprovalNotes.Should().StartWith("alias-curation:").And.Contain(";source:auto;");

        var canonical = await db.DeliveryNeighborhoods.SingleAsync(x => x.Id == fixture.Canonical.Id);
        canonical.Neighborhood.Should().Be("JARDIM DAS FLORES");
        canonical.DisplayName.Should().Be("JARDIM DAS FLORES");

        var legacy = await db.DeliveryNeighborhoods.SingleAsync(x => x.Id == fixture.AliasRow.Id);
        legacy.Neighborhood.Should().Be("Jardim Flores");
        legacy.IsActive.Should().BeTrue();
        legacy.DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task Apply_is_idempotent_for_the_same_manifest()
    {
        var fixture = await CreateFixtureAsync();

        await using var db = fixture.Db;
        var service = new NeighborhoodAliasApplyService(db);
        await service.ApplyAsync(fixture.Manifest);

        var second = await service.ApplyAsync(fixture.Manifest);

        second.AliasesInserted.Should().Be(0);
        second.AliasesUnchanged.Should().Be(1);
        (await db.DeliveryNeighborhoodAliases.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Rollback_deactivates_the_alias_and_clears_the_display_name_idempotently()
    {
        var fixture = await CreateFixtureAsync();

        await using var db = fixture.Db;
        var service = new NeighborhoodAliasApplyService(db);
        await service.ApplyAsync(fixture.Manifest);

        var result = await service.RollbackAsync(fixture.Manifest);

        result.AliasesDeactivated.Should().Be(1);
        result.DisplayNamesCleared.Should().Be(1);

        var alias = await db.DeliveryNeighborhoodAliases.SingleAsync();
        alias.IsActive.Should().BeFalse();
        alias.IsApproved.Should().BeFalse();
        alias.ApprovalNotes.Should().StartWith("alias-curation-rollback:");

        var canonical = await db.DeliveryNeighborhoods.SingleAsync(x => x.Id == fixture.Canonical.Id);
        canonical.DisplayName.Should().BeNull();

        var again = await service.RollbackAsync(fixture.Manifest);
        again.AliasesDeactivated.Should().Be(0);
        again.DisplayNamesCleared.Should().Be(0);
    }

    [Fact]
    public async Task Apply_reactivates_a_previously_rolled_back_alias()
    {
        var fixture = await CreateFixtureAsync();

        await using var db = fixture.Db;
        var service = new NeighborhoodAliasApplyService(db);
        await service.ApplyAsync(fixture.Manifest);
        await service.RollbackAsync(fixture.Manifest);

        var result = await service.ApplyAsync(fixture.Manifest);

        result.AliasesInserted.Should().Be(0);
        result.AliasesReactivated.Should().Be(1);
        (await db.DeliveryNeighborhoodAliases.CountAsync()).Should().Be(1);
        (await db.DeliveryNeighborhoodAliases.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_refuses_a_tampered_manifest()
    {
        var fixture = await CreateFixtureAsync();
        var original = fixture.Manifest;
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
                .Select(decision => decision with { DisplayName = decision.DisplayName + " ALTERADO" })
                .ToList()
        };

        await using var db = fixture.Db;
        var act = () => new NeighborhoodAliasApplyService(db).ApplyAsync(tampered);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Hash*");
        (await db.DeliveryNeighborhoodAliases.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Apply_refuses_a_canonical_from_another_city()
    {
        var fixture = await CreateFixtureAsync(canonicalInOtherCity: true);

        await using var db = fixture.Db;
        var act = () => new NeighborhoodAliasApplyService(db).ApplyAsync(fixture.Manifest);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outra cidade*");
        (await db.DeliveryNeighborhoodAliases.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Apply_remaps_a_group_linked_to_a_store_delivery_area()
    {
        var fixture = await CreateFixtureAsync(linkStoreAreaToAlias: true);

        await using var db = fixture.Db;
        var result = await new NeighborhoodAliasApplyService(db).ApplyAsync(fixture.Manifest);

        result.AliasesInserted.Should().Be(1);
        result.StoreAreasRemapped.Should().Be(1);
        result.StoreAreasRemovedAsDuplicate.Should().Be(0);

        var area = await db.StoreDeliveryAreas.SingleAsync();
        area.NeighborhoodId.Should().Be(fixture.Canonical.Id);
        area.Neighborhood.Should().Be(fixture.Canonical.Neighborhood);
        area.DeliveryFee.Should().Be(5m);
        area.IsActive.Should().BeTrue();
        area.Notes.Should().BeEmpty();
        (await db.DeliveryNeighborhoodAliases.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Apply_removes_a_duplicate_store_area_deterministically_and_audits_it()
    {
        var fixture = await CreateFixtureAsync(linkStoreAreaToAlias: true, existingCanonicalArea: true);

        await using var db = fixture.Db;
        var result = await new NeighborhoodAliasApplyService(db).ApplyAsync(fixture.Manifest);

        result.StoreAreasRemovedAsDuplicate.Should().Be(1);
        var area = await db.StoreDeliveryAreas.SingleAsync();
        area.NeighborhoodId.Should().Be(fixture.Canonical.Id);
        area.DeliveryFee.Should().Be(12.5m);
        (await db.AuditLogs.CountAsync(log =>
            log.Event == "NeighborhoodStoreAreaDuplicateRemoved")).Should().Be(1);
    }

    [Fact]
    public async Task Apply_refuses_a_conflicting_active_alias_in_the_same_city()
    {
        var fixture = await CreateFixtureAsync();

        await using var db = fixture.Db;
        db.DeliveryNeighborhoodAliases.Add(new DeliveryNeighborhoodAlias
        {
            CityId = fixture.Canonical.CityId!.Value,
            CanonicalNeighborhoodId = Guid.NewGuid(),
            Alias = "JARDIM FLORES",
            NormalizedAlias = "jardim flores",
            IsActive = true,
            IsApproved = true
        });
        await db.SaveChangesAsync();

        var act = () => new NeighborhoodAliasApplyService(db).ApplyAsync(fixture.Manifest);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*alias ativo*");
    }

    [Fact]
    public async Task Apply_preserves_a_display_name_that_was_already_curated()
    {
        var fixture = await CreateFixtureAsync(existingDisplayName: "Jardim das Flores (oficial)");

        await using var db = fixture.Db;
        var result = await new NeighborhoodAliasApplyService(db).ApplyAsync(fixture.Manifest);

        result.DisplayNamesSet.Should().Be(0);
        result.Warnings.Should().Contain(warning => warning.Contains("preservado"));
        var canonical = await db.DeliveryNeighborhoods.SingleAsync(x => x.Id == fixture.Canonical.Id);
        canonical.DisplayName.Should().Be("Jardim das Flores (oficial)");
    }

    [Fact]
    public async Task Reviewed_decisions_are_applied_with_reviewer_audit()
    {
        var fixture = await CreateFixtureAsync(reviewed: true);

        await using var db = fixture.Db;
        var result = await new NeighborhoodAliasApplyService(db).ApplyAsync(fixture.Manifest);

        result.AliasesInserted.Should().Be(1);
        var alias = await db.DeliveryNeighborhoodAliases.SingleAsync();
        alias.ApprovalNotes.Should().Contain(";source:review;")
            .And.Contain(";reviewer:arquiteto;")
            .And.Contain(";reviewed:2026-11-16T12:00:00Z;");
        alias.ApprovedByUserId.Should().Be(Guid.Parse("55555555-5555-4555-8555-555555555555"));
        alias.NormalizedAlias.Should().Be("residencial terra do himalaia");

        var canonical = await db.DeliveryNeighborhoods.SingleAsync(x => x.Id == fixture.Canonical.Id);
        canonical.DisplayName.Should().Be("RESIDENCIAL TERRAS DO HIMALÁIA");
    }

    [Fact]
    public async Task Repairs_assign_an_unambiguous_city_and_deactivate_test_data_auditabl()
    {
        await using var db = CreateDbContext();
        var city = new City { Name = "Rio das Ostras", NormalizedName = "rio das ostras", Uf = "RJ" };
        db.Cities.Add(city);

        var repaired = new DeliveryNeighborhood
        {
            City = "Rio das Ostras",
            Neighborhood = "Bairro Novo",
            NormalizedName = "bairro novo",
            Kind = GeographicProvenance.NeighborhoodKind.Manual,
            IsActive = true
        };
        var testData = new DeliveryNeighborhood
        {
            City = "Maricá",
            Neighborhood = "teste",
            NormalizedName = "teste",
            Kind = GeographicProvenance.NeighborhoodKind.Manual,
            IsActive = true
        };
        db.DeliveryNeighborhoods.AddRange(repaired, testData);
        await db.SaveChangesAsync();

        var repairs = new List<NeighborhoodCurationRepair>
        {
            new()
            {
                Kind = "set_city",
                NeighborhoodId = repaired.Id.ToString(),
                CityId = city.Id.ToString(),
                CityName = "Rio das Ostras",
                Uf = "RJ",
                Reason = "cidade inequivoca",
                Reviewer = "curadoria-nacional",
                ReviewedAtUtc = "2026-11-15T12:00:00Z"
            },
            new()
            {
                Kind = "deactivate",
                NeighborhoodId = testData.Id.ToString(),
                Reason = "registro de teste",
                Reviewer = "curadoria-nacional",
                ReviewedAtUtc = "2026-11-15T12:00:00Z"
            }
        };

        var hash = NeighborhoodCurationManifestHasher.ComputeDecisionHash(
            NeighborhoodAliasCurationPolicy.Version,
            QueueHash,
            null,
            [],
            repairs);

        var manifest = new NeighborhoodCurationManifest
        {
            ManifestHash = hash,
            Inputs = new NeighborhoodCurationManifestInputs { QueueSha256 = QueueHash },
            Repairs = repairs
        };

        var result = await new NeighborhoodAliasApplyService(db).ApplyAsync(manifest);

        result.RepairsApplied.Should().Be(2);
        (await db.DeliveryNeighborhoods.SingleAsync(x => x.Id == repaired.Id)).CityId.Should().Be(city.Id);
        (await db.DeliveryNeighborhoods.SingleAsync(x => x.Id == testData.Id)).IsActive.Should().BeFalse();
        (await db.AuditLogs.CountAsync()).Should().Be(2);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-alias-apply-{Guid.NewGuid()}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<Fixture> CreateFixtureAsync(
        bool canonicalInOtherCity = false,
        bool linkStoreAreaToAlias = false,
        bool existingCanonicalArea = false,
        string? existingDisplayName = null,
        bool reviewed = false)
    {
        var db = CreateDbContext();
        var city = new City { Name = "Cidade Teste", NormalizedName = "cidade teste", Uf = "SP" };
        var otherCity = new City { Name = "Outra Cidade", NormalizedName = "outra cidade", Uf = "SP" };
        db.Cities.AddRange(city, otherCity);

        var canonicalName = reviewed ? "RESIDENCIAL TERRAS DO HIMALAIA" : "JARDIM DAS FLORES";
        var aliasName = reviewed ? "Residencial Terra do Himaláia" : "Jardim Flores";
        var recommendedName = reviewed ? "Residencial Terras do Himaláia" : "JARDIM DAS FLORES";

        var canonical = new DeliveryNeighborhood
        {
            CityId = canonicalInOtherCity ? otherCity.Id : city.Id,
            City = "Cidade Teste",
            Neighborhood = canonicalName,
            NormalizedName = NeighborhoodName.Normalize(canonicalName),
            Kind = GeographicProvenance.NeighborhoodKind.OfficialCnefe,
            IsActive = true,
            DisplayName = existingDisplayName
        };

        var aliasRow = new DeliveryNeighborhood
        {
            CityId = city.Id,
            City = "Cidade Teste",
            Neighborhood = aliasName,
            NormalizedName = NeighborhoodName.Normalize(aliasName),
            Kind = GeographicProvenance.NeighborhoodKind.Legacy,
            IsActive = true
        };

        db.DeliveryNeighborhoods.AddRange(canonical, aliasRow);

        if (linkStoreAreaToAlias)
        {
            var storeId = Guid.NewGuid();
            db.StoreDeliveryAreas.Add(new StoreDeliveryArea
            {
                StoreId = storeId,
                NeighborhoodId = aliasRow.Id,
                Neighborhood = aliasRow.Neighborhood,
                DeliveryFee = 5m
            });

            if (existingCanonicalArea)
            {
                db.StoreDeliveryAreas.Add(new StoreDeliveryArea
                {
                    StoreId = storeId,
                    NeighborhoodId = canonical.Id,
                    Neighborhood = canonical.Neighborhood,
                    DeliveryFee = 12.5m,
                    IsActive = true,
                    Notes = "configuracao existente"
                });
            }
        }

        await db.SaveChangesAsync();

        var queue = new NeighborhoodCurationQueueDocument
        {
            Schema = "urbeat.neighborhood.uppercase-approval-queue/1",
            Groups =
            [
                new NeighborhoodCurationQueueGroup
                {
                    PriorityRank = 1,
                    Grupo = reviewed ? "G000100" : "G000101",
                    Uf = "SP",
                    Ibge = "3550308",
                    Cidade = "Cidade Teste",
                    CityId = city.Id.ToString(),
                    CanonicalId = canonical.Id.ToString(),
                    CanonicalName = canonicalName,
                    CanonicalKind = GeographicProvenance.NeighborhoodKind.OfficialCnefe,
                    CanonicalSource = "cnefe",
                    Distance = 0.2,
                    DistanceMinKm = 0.2,
                    CnefeSourceOk = true,
                    AccentInferiority = reviewed,
                    AccentRestorationSafe = true,
                    RecommendedCanonicalName = recommendedName,
                    Members =
                    [
                        new NeighborhoodCurationQueueMember
                        {
                            Id = canonical.Id.ToString(),
                            Name = canonicalName,
                            Kind = GeographicProvenance.NeighborhoodKind.OfficialCnefe,
                            Source = "cnefe",
                            IsUpper = true
                        },
                        new NeighborhoodCurationQueueMember
                        {
                            Id = aliasRow.Id.ToString(),
                            Name = aliasName,
                            Kind = GeographicProvenance.NeighborhoodKind.Legacy,
                            Source = "legacy",
                            IsUpper = false
                        }
                    ],
                    Flags = []
                }
            ]
        };

        NeighborhoodCurationApprovalsDocument? approvals = null;
        string? approvalsHash = null;
        if (reviewed)
        {
            approvals = new NeighborhoodCurationApprovalsDocument
            {
                Approvals =
                [
                    new NeighborhoodCurationApproval
                    {
                        GroupKey = "G000100",
                        Reviewer = "arquiteto",
                        ReviewerUserId = "55555555-5555-4555-8555-555555555555",
                        ReviewedAtUtc = "2026-11-16T12:00:00Z",
                        Note = "Grafia confirmada.",
                        ConfirmedCanonicalName = recommendedName,
                        ConfirmedDisplayName = "RESIDENCIAL TERRAS DO HIMALÁIA",
                        ConfirmedAliasNames = [aliasName]
                    }
                ]
            };
            approvalsHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        }

        var manifest = new NeighborhoodAliasCurator().BuildManifest(
            queue,
            "queue.json",
            QueueHash,
            approvals,
            approvals is null ? null : "approvals.json",
            approvalsHash);

        return new Fixture(db, manifest, canonical, aliasRow);
    }

    private sealed record Fixture(
        ApplicationDbContext Db,
        NeighborhoodCurationManifest Manifest,
        DeliveryNeighborhood Canonical,
        DeliveryNeighborhood AliasRow);
}
