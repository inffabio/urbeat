using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Urbeat.Application.Geo.Curation;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Geo;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Curation;

public sealed class NeighborhoodAliasApplyOptions
{
    /// <summary>Rollback also clears the display label when it still equals the manifest value.</summary>
    public bool ResetDisplayNames { get; init; } = true;

    /// <summary>Rollback also restores the canonical name when it still equals the manifest value.</summary>
    public bool RestoreCanonicalNames { get; init; } = true;
}

public sealed class NeighborhoodAliasApplyResult
{
    public DateTime AppliedAtUtc { get; init; }

    public bool DryRun { get; init; }

    public int AliasesInserted { get; init; }

    public int AliasesReactivated { get; init; }

    public int AliasesUnchanged { get; init; }

    public int AliasesDeactivated { get; init; }

    public int DisplayNamesSet { get; init; }

    public int DisplayNamesCleared { get; init; }

    public int CanonicalNamesUpdated { get; init; }

    public int CanonicalNamesRestored { get; init; }

    public int StoreAreasRemapped { get; init; }

    public int StoreAreasRemovedAsDuplicate { get; init; }

    public int RepairsApplied { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Applies or rolls back an approved curation manifest. The service is authoritative: it revalidates
/// every decision against the database (city, canonical identity, active rows, store delivery areas
/// and alias conflicts) and derives the alias identifier from the manifest hash so re-running the
/// same manifest is idempotent.
///
/// Canonical <see cref="DeliveryNeighborhood.Neighborhood"/> values are never merged, deactivated or
/// deleted. The only accepted rename is an accent/case correction whose normalized value is
/// unchanged. Store delivery areas are remapped to the canonical identifier while preserving store,
/// fee, notes, status and audit fields; a duplicate area is removed only after being logged.
/// </summary>
public sealed class NeighborhoodAliasApplyService
{
    private const int ApprovalNotesMaxLength = 500;
    private const string AuditPrefix = "alias-curation:";
    private const string RollbackPrefix = "alias-curation-rollback:";

    private readonly ApplicationDbContext _db;

    public NeighborhoodAliasApplyService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>Read-only validation with the same rules as <see cref="ApplyAsync"/>.</summary>
    public Task<NeighborhoodAliasApplyResult> DryRunAsync(
        NeighborhoodCurationManifest manifest,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        return ProcessAsync(manifest, apply: false, nowUtc, cancellationToken);
    }

    public Task<NeighborhoodAliasApplyResult> ApplyAsync(
        NeighborhoodCurationManifest manifest,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        return ProcessAsync(manifest, apply: true, nowUtc, cancellationToken);
    }

    public async Task<NeighborhoodAliasApplyResult> RollbackAsync(
        NeighborhoodCurationManifest manifest,
        NeighborhoodAliasApplyOptions? options = null,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new NeighborhoodAliasApplyOptions();
        var rolledBackAtUtc = ValidateManifest(manifest, nowUtc);

        var warnings = new List<string>();
        var deactivated = 0;
        var displayNamesCleared = 0;
        var canonicalNamesRestored = 0;

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        foreach (var decision in manifest.Decisions.Where(decision => decision.Applyable))
        {
            if (!Guid.TryParse(decision.CityId, out var cityId))
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': CityId invalido no manifesto.");
            }

            var expectedPrefix = BuildAuditPrefix(manifest.ManifestHash);

            foreach (var alias in decision.Aliases)
            {
                var aliasId = ResolveAliasId(manifest, decision, alias, cityId);
                var row = await _db.DeliveryNeighborhoodAliases
                    .FirstOrDefaultAsync(x => x.Id == aliasId, cancellationToken);

                if (row is null)
                {
                    warnings.Add($"{decision.GroupKey}: alias {alias.Name} nao encontrado; nada a reverter.");
                    continue;
                }

                if (!row.IsActive)
                {
                    continue;
                }

                if (row.ApprovalNotes is null
                    || !row.ApprovalNotes.StartsWith(expectedPrefix, StringComparison.Ordinal)
                    || !row.ApprovalNotes.Contains($";group:{decision.GroupKey};", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{decision.GroupKey}: alias {alias.Name} nao foi criado por este manifesto; rollback recusado.");
                }

                row.IsActive = false;
                row.IsApproved = false;
                row.ApprovalNotes = BuildRollbackNotes(manifest.ManifestHash, decision.GroupKey, rolledBackAtUtc);
                row.MarkAsUpdated();
                deactivated++;
            }

            if (!Guid.TryParse(decision.CanonicalNeighborhoodId, out var canonicalId))
            {
                continue;
            }

            var canonical = await _db.DeliveryNeighborhoods
                .FirstOrDefaultAsync(x => x.Id == canonicalId, cancellationToken);

            if (canonical is null)
            {
                continue;
            }

            if (options.ResetDisplayNames
                && !string.IsNullOrWhiteSpace(canonical.DisplayName)
                && string.Equals(canonical.DisplayName, decision.DisplayName, StringComparison.Ordinal))
            {
                canonical.DisplayName = null;
                canonical.MarkAsUpdated();
                displayNamesCleared++;
            }

            if (options.RestoreCanonicalNames
                && decision.UpdateCanonicalName
                && string.Equals(canonical.Neighborhood, decision.RecommendedCanonicalName, StringComparison.Ordinal)
                && !string.Equals(canonical.Neighborhood, decision.CanonicalName, StringComparison.Ordinal))
            {
                canonical.Neighborhood = decision.CanonicalName;
                canonical.NormalizedName = NeighborhoodName.Normalize(decision.CanonicalName);
                canonical.MarkAsUpdated();
                canonicalNamesRestored++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new NeighborhoodAliasApplyResult
        {
            AppliedAtUtc = rolledBackAtUtc,
            AliasesDeactivated = deactivated,
            DisplayNamesCleared = displayNamesCleared,
            CanonicalNamesRestored = canonicalNamesRestored,
            Warnings = warnings
        };
    }

    private async Task<NeighborhoodAliasApplyResult> ProcessAsync(
        NeighborhoodCurationManifest manifest,
        bool apply,
        DateTimeOffset? nowUtc,
        CancellationToken cancellationToken)
    {
        var appliedAtUtc = ValidateManifest(manifest, nowUtc);

        var warnings = new List<string>();
        var inserted = 0;
        var reactivated = 0;
        var unchanged = 0;
        var displayNamesSet = 0;
        var canonicalNamesUpdated = 0;
        var storeAreasRemapped = 0;
        var storeAreasRemoved = 0;

        IDbContextTransaction? transaction = null;
        if (apply)
        {
            transaction = await BeginTransactionAsync(cancellationToken);
        }

        try
        {
            foreach (var decision in manifest.Decisions.Where(decision => decision.Applyable))
            {
                var outcome = await ProcessDecisionAsync(
                    manifest,
                    decision,
                    appliedAtUtc,
                    apply,
                    warnings,
                    cancellationToken);

                inserted += outcome.Inserted;
                reactivated += outcome.Reactivated;
                unchanged += outcome.Unchanged;
                displayNamesSet += outcome.DisplayNamesSet;
                canonicalNamesUpdated += outcome.CanonicalNamesUpdated;
                storeAreasRemapped += outcome.StoreAreasRemapped;
                storeAreasRemoved += outcome.StoreAreasRemoved;
            }

            var repairsApplied = 0;
            foreach (var repair in manifest.Repairs)
            {
                if (await ProcessRepairAsync(manifest, repair, apply, warnings, cancellationToken))
                {
                    repairsApplied++;
                }
            }

            if (apply)
            {
                await _db.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
            }

            return new NeighborhoodAliasApplyResult
            {
                AppliedAtUtc = appliedAtUtc,
                DryRun = !apply,
                AliasesInserted = inserted,
                AliasesReactivated = reactivated,
                AliasesUnchanged = unchanged,
                DisplayNamesSet = displayNamesSet,
                CanonicalNamesUpdated = canonicalNamesUpdated,
                StoreAreasRemapped = storeAreasRemapped,
                StoreAreasRemovedAsDuplicate = storeAreasRemoved,
                RepairsApplied = repairsApplied,
                Warnings = warnings
            };
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private sealed record DecisionApplyOutcome(
        int Inserted,
        int Reactivated,
        int Unchanged,
        int DisplayNamesSet,
        int CanonicalNamesUpdated,
        int StoreAreasRemapped,
        int StoreAreasRemoved);

    private async Task<DecisionApplyOutcome> ProcessDecisionAsync(
        NeighborhoodCurationManifest manifest,
        NeighborhoodCurationDecision decision,
        DateTime appliedAtUtc,
        bool apply,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(decision.CityId, out var cityId)
            || !Guid.TryParse(decision.CanonicalNeighborhoodId, out var canonicalId))
        {
            throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': CityId/canonical ausente ou invalido no manifesto.");
        }

        var canonical = await _db.DeliveryNeighborhoods
            .FirstOrDefaultAsync(x => x.Id == canonicalId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': bairro canonico {canonicalId} nao encontrado.");

        if (canonical.CityId != cityId)
        {
            throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': canonico pertence a outra cidade; alias nunca atravessa cidade.");
        }

        if (!canonical.IsActive)
        {
            throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': canonico esta inativo desde o dry-run.");
        }

        var currentCanonicalName = NeighborhoodName.Normalize(canonical.Neighborhood);
        var expectedCanonicalName = NeighborhoodName.Normalize(decision.CanonicalName);
        var desiredCanonicalName = decision.UpdateCanonicalName
            ? decision.RecommendedCanonicalName.Trim()
            : decision.CanonicalName.Trim();

        if (desiredCanonicalName.Length == 0)
        {
            throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': nome canonico vazio no manifesto.");
        }

        if (!string.Equals(
                NeighborhoodName.Normalize(desiredCanonicalName),
                expectedCanonicalName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': a correcao de nome canonico muda letras, ordem ou plural.");
        }

        var canonicalNameChanged = !string.Equals(
            currentCanonicalName,
            expectedCanonicalName,
            StringComparison.Ordinal);

        if (canonicalNameChanged
            && !string.Equals(currentCanonicalName, NeighborhoodName.Normalize(desiredCanonicalName), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': nome do canonico mudou desde o dry-run.");
        }

        var aliasRows = new List<(NeighborhoodCurationAliasDecision Alias, Guid AliasId, Guid NeighborhoodId)>();
        foreach (var alias in decision.Aliases)
        {
            var aliasId = ResolveAliasId(manifest, decision, alias, cityId);

            if (!Guid.TryParse(alias.NeighborhoodId, out var aliasNeighborhoodId))
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': identificador do alias invalido ({alias.Name}).");
            }

            if (aliasNeighborhoodId == canonicalId)
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': alias aponta para o proprio canonico.");
            }

            var row = await _db.DeliveryNeighborhoods
                .FirstOrDefaultAsync(x => x.Id == aliasNeighborhoodId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': bairro do alias {aliasNeighborhoodId} nao encontrado.");

            if (row.CityId != cityId)
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': alias {alias.Name} pertence a outra cidade.");
            }

            if (!row.IsActive)
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': alias {alias.Name} esta inativo desde o dry-run.");
            }

            if (!string.Equals(NeighborhoodName.Normalize(row.Neighborhood), alias.NormalizedName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': nome do alias {alias.Name} mudou desde o dry-run.");
            }

            aliasRows.Add((alias, aliasId, aliasNeighborhoodId));
        }

        var areaRemap = await RemapStoreAreasAsync(
            decision,
            aliasRows.Select(aliasRow => aliasRow.NeighborhoodId).ToList(),
            canonical,
            apply,
            cancellationToken);

        var canonicalNamesUpdated = 0;
        if (!canonicalNameChanged)
        {
            if (decision.UpdateCanonicalName
                && !string.Equals(canonical.Neighborhood, desiredCanonicalName, StringComparison.Ordinal)
                && string.Equals(
                    NeighborhoodName.Normalize(canonical.Neighborhood),
                    NeighborhoodName.Normalize(desiredCanonicalName),
                    StringComparison.Ordinal))
            {
                if (apply)
                {
                    canonical.Neighborhood = desiredCanonicalName;
                    canonical.NormalizedName = NeighborhoodName.Normalize(desiredCanonicalName);
                    canonical.MarkAsUpdated();
                }

                canonicalNamesUpdated = 1;
            }
        }
        else if (apply)
        {
            canonical.Neighborhood = desiredCanonicalName;
            canonical.NormalizedName = NeighborhoodName.Normalize(desiredCanonicalName);
            canonical.MarkAsUpdated();
            canonicalNamesUpdated = 1;
        }
        else
        {
            canonicalNamesUpdated = 1;
        }

        var displayNamesSet = 0;
        if (string.IsNullOrWhiteSpace(canonical.DisplayName))
        {
            if (apply)
            {
                canonical.DisplayName = decision.DisplayName;
                canonical.MarkAsUpdated();
            }

            displayNamesSet = 1;
        }
        else if (!string.Equals(canonical.DisplayName, decision.DisplayName, StringComparison.Ordinal))
        {
            warnings.Add(
                $"{decision.GroupKey}: DisplayName existente preservado ('{canonical.DisplayName}').");
        }

        var notes = BuildAuditNotes(manifest, decision, appliedAtUtc);
        var approvedByUserId = ResolveReviewerUserId(decision.Review);

        var inserted = 0;
        var reactivated = 0;
        var unchanged = 0;

        foreach (var (alias, aliasId, aliasNeighborhoodId) in aliasRows)
        {
            var existing = await _db.DeliveryNeighborhoodAliases
                .FirstOrDefaultAsync(x => x.Id == aliasId, cancellationToken);

            if (existing is not null)
            {
                if (existing.CityId != cityId || existing.CanonicalNeighborhoodId != canonicalId)
                {
                    throw new InvalidOperationException(
                        $"Grupo '{decision.GroupKey}': alias {alias.Name} ja existe apontando para outro canonico.");
                }

                if (existing.IsActive && existing.IsApproved)
                {
                    unchanged++;
                    continue;
                }

                if (apply)
                {
                    existing.Alias = alias.Name;
                    existing.NormalizedAlias = alias.NormalizedName;
                    existing.IsActive = true;
                    existing.IsApproved = true;
                    existing.ApprovedAtUtc = appliedAtUtc;
                    existing.ApprovedByUserId = approvedByUserId;
                    existing.ApprovalNotes = notes;
                    existing.MarkAsUpdated();
                }

                reactivated++;
                continue;
            }

            var conflict = await _db.DeliveryNeighborhoodAliases.AnyAsync(
                x => x.CityId == cityId
                     && x.NormalizedAlias == alias.NormalizedName
                     && x.IsActive
                     && x.Id != aliasId,
                cancellationToken);

            if (conflict)
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': ja existe alias ativo para '{alias.NormalizedName}' nesta cidade.");
            }

            if (apply)
            {
                _db.DeliveryNeighborhoodAliases.Add(new DeliveryNeighborhoodAlias(aliasId)
                {
                    CityId = cityId,
                    CanonicalNeighborhoodId = canonicalId,
                    Alias = alias.Name,
                    NormalizedAlias = alias.NormalizedName,
                    IsActive = true,
                    IsApproved = true,
                    ApprovedAtUtc = appliedAtUtc,
                    ApprovedByUserId = approvedByUserId,
                    ApprovalNotes = notes
                });
            }

            inserted++;

            if (apply)
            {
                AddAudit(
                    "NeighborhoodAliasApplied",
                    nameof(DeliveryNeighborhoodAlias),
                    aliasId,
                    $"manifest={ShortHash(manifest.ManifestHash)};group={decision.GroupKey};" +
                    $"city={cityId};canonical={canonicalId};source={aliasNeighborhoodId};alias={alias.Name}");
            }
        }

        if (apply && (areaRemap.Remapped > 0 || areaRemap.RemovedDuplicates > 0))
        {
            AddAudit(
                "NeighborhoodStoreAreasRemapped",
                nameof(DeliveryNeighborhood),
                canonicalId,
                $"manifest={ShortHash(manifest.ManifestHash)};group={decision.GroupKey};" +
                $"remapped={areaRemap.Remapped};duplicates={areaRemap.RemovedDuplicates}");
        }

        return new DecisionApplyOutcome(
            inserted,
            reactivated,
            unchanged,
            displayNamesSet,
            canonicalNamesUpdated,
            areaRemap.Remapped,
            areaRemap.RemovedDuplicates);
    }

    private sealed record AreaRemapOutcome(int Remapped, int RemovedDuplicates);

    private async Task<AreaRemapOutcome> RemapStoreAreasAsync(
        NeighborhoodCurationDecision decision,
        IReadOnlyCollection<Guid> aliasNeighborhoodIds,
        DeliveryNeighborhood canonical,
        bool apply,
        CancellationToken cancellationToken)
    {
        if (aliasNeighborhoodIds.Count == 0)
        {
            return new AreaRemapOutcome(0, 0);
        }

        var aliasIdList = aliasNeighborhoodIds.ToList();
        var aliasAreas = await _db.StoreDeliveryAreas
            .Where(area => area.NeighborhoodId != null && aliasIdList.Contains(area.NeighborhoodId.Value))
            .ToListAsync(cancellationToken);

        if (aliasAreas.Count == 0)
        {
            return new AreaRemapOutcome(0, 0);
        }

        var storeIds = aliasAreas.Select(area => area.StoreId).Distinct().ToList();
        var storeAreas = await _db.StoreDeliveryAreas
            .Where(area => storeIds.Contains(area.StoreId) && area.NeighborhoodId != null)
            .ToListAsync(cancellationToken);

        var remapped = 0;
        var removed = 0;

        foreach (var storeId in storeIds)
        {
            var areas = storeAreas.Where(area => area.StoreId == storeId).ToList();
            var canonicalArea = areas.FirstOrDefault(area => area.NeighborhoodId == canonical.Id);
            var aliasStoreAreas = areas
                .Where(area => area.NeighborhoodId != null && aliasIdList.Contains(area.NeighborhoodId.Value))
                .ToList();

            if (aliasStoreAreas.Count == 0)
            {
                continue;
            }

            var candidates = aliasStoreAreas
                .Concat(canonicalArea is null ? [] : [canonicalArea])
                .ToList();

            // Active status wins; the existing canonical row wins a tie so an already-linked store
            // keeps its configuration. The remaining order is deterministic.
            var winner = candidates
                .OrderByDescending(area => area.IsActive)
                .ThenBy(area => area.NeighborhoodId == canonical.Id ? 0 : 1)
                .ThenByDescending(area => area.UpdatedAtUtc ?? area.CreatedAtUtc)
                .ThenBy(area => area.CreatedAtUtc)
                .ThenBy(area => area.Id)
                .First();

            var losers = candidates
                .Where(area => !ReferenceEquals(area, winner))
                .ToList();

            if (apply && losers.Count > 0)
            {
                foreach (var loser in losers)
                {
                    AddAudit(
                        "NeighborhoodStoreAreaDuplicateRemoved",
                        nameof(StoreDeliveryArea),
                        loser.Id,
                        $"group={decision.GroupKey};store={loser.StoreId};" +
                        $"neighborhoodId={loser.NeighborhoodId};fee={loser.DeliveryFee.ToString(CultureInfo.InvariantCulture)};" +
                        $"active={loser.IsActive};notes={Truncate(loser.Notes, 80)}");
                }

                _db.StoreDeliveryAreas.RemoveRange(losers);
                await _db.SaveChangesAsync(cancellationToken);
                removed += losers.Count;
            }
            else
            {
                removed += losers.Count;
            }

            if (winner.NeighborhoodId != canonical.Id
                || !string.Equals(winner.Neighborhood, canonical.Neighborhood, StringComparison.Ordinal))
            {
                if (apply)
                {
                    winner.NeighborhoodId = canonical.Id;
                    winner.Neighborhood = canonical.Neighborhood;
                    winner.MarkAsUpdated();
                }

                remapped++;
            }
        }

        return new AreaRemapOutcome(remapped, removed);
    }

    private async Task<bool> ProcessRepairAsync(
        NeighborhoodCurationManifest manifest,
        NeighborhoodCurationRepair repair,
        bool apply,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(repair.NeighborhoodId, out var neighborhoodId))
        {
            throw new InvalidOperationException($"Reparo com NeighborhoodId invalido: '{repair.NeighborhoodId}'.");
        }

        var neighborhood = await _db.DeliveryNeighborhoods
            .FirstOrDefaultAsync(x => x.Id == neighborhoodId, cancellationToken)
            ?? throw new InvalidOperationException($"Reparo: bairro {neighborhoodId} nao encontrado.");

        if (repair.Kind == "set_city")
        {
            if (!Guid.TryParse(repair.CityId, out var cityId))
            {
                throw new InvalidOperationException($"Reparo set_city {neighborhoodId}: CityId invalido.");
            }

            var city = await _db.Cities
                .FirstOrDefaultAsync(x => x.Id == cityId, cancellationToken)
                ?? throw new InvalidOperationException($"Reparo set_city {neighborhoodId}: cidade {cityId} nao encontrada.");

            if (!string.Equals(NeighborhoodName.Normalize(city.Name), NeighborhoodName.Normalize(repair.CityName), StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(repair.Uf)
                    && !string.Equals(city.Uf, repair.Uf, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Reparo set_city {neighborhoodId}: cidade informada nao corresponde a {city.Name}/{city.Uf}.");
            }

            if (neighborhood.CityId == cityId)
            {
                return false;
            }

            if (neighborhood.CityId is not null)
            {
                throw new InvalidOperationException(
                    $"Reparo set_city {neighborhoodId}: registro ja possui CityId {neighborhood.CityId}; correcao recusada.");
            }

            var linkedAreas = await _db.StoreDeliveryAreas
                .CountAsync(area => area.NeighborhoodId == neighborhoodId && area.IsActive, cancellationToken);

            if (linkedAreas > 0)
            {
                warnings.Add($"Reparo set_city {neighborhoodId}: {linkedAreas} area(s) ativa(s) vinculada(s).");
            }

            if (apply)
            {
                neighborhood.CityId = cityId;
                neighborhood.City = city.Name;
                neighborhood.MarkAsUpdated();
                AddAudit(
                    "NeighborhoodCityRepaired",
                    nameof(DeliveryNeighborhood),
                    neighborhoodId,
                    $"manifest={ShortHash(manifest.ManifestHash)};city={city.Id};uf={city.Uf};reason={repair.Reason}");
            }

            return true;
        }

        if (repair.Kind == "deactivate")
        {
            if (!neighborhood.IsActive)
            {
                return false;
            }

            var linkedAreas = await _db.StoreDeliveryAreas
                .CountAsync(area => area.NeighborhoodId == neighborhoodId && area.IsActive, cancellationToken);

            if (linkedAreas > 0)
            {
                throw new InvalidOperationException(
                    $"Reparo deactivate {neighborhoodId}: {linkedAreas} area(s) ativa(s) dependem do bairro.");
            }

            if (apply)
            {
                neighborhood.IsActive = false;
                neighborhood.MarkAsUpdated();
                AddAudit(
                    "NeighborhoodDeactivatedByCuration",
                    nameof(DeliveryNeighborhood),
                    neighborhoodId,
                    $"manifest={ShortHash(manifest.ManifestHash)};reason={repair.Reason}");
            }

            return true;
        }

        throw new InvalidOperationException($"Reparo com tipo invalido: '{repair.Kind}'.");
    }

    private void AddAudit(string eventName, string entity, Guid entityId, string description)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Event = eventName,
            Entity = entity,
            EntityId = entityId,
            Description = Truncate(description, 500)
        });
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static Guid ResolveAliasId(
        NeighborhoodCurationManifest manifest,
        NeighborhoodCurationDecision decision,
        NeighborhoodCurationAliasDecision alias,
        Guid cityId)
    {
        if (!Guid.TryParse(alias.AliasId, out var storedAliasId))
        {
            throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': AliasId ausente no manifesto.");
        }

        var expected = NeighborhoodCurationManifestHasher.AliasIdFor(
            manifest.ManifestHash,
            cityId,
            alias.NormalizedName);

        if (storedAliasId != expected)
        {
            throw new InvalidOperationException(
                $"Grupo '{decision.GroupKey}': AliasId de '{alias.Name}' nao corresponde ao manifesto.");
        }

        return expected;
    }

    private static Guid? ResolveReviewerUserId(NeighborhoodCurationReview? review)
    {
        return review is not null && Guid.TryParse(review.ReviewerUserId, out var userId)
            ? userId
            : null;
    }

    private static DateTime ValidateManifest(NeighborhoodCurationManifest manifest, DateTimeOffset? nowUtc)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!string.Equals(manifest.Schema, NeighborhoodCurationManifestSchema.Current, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Schema de manifesto nao suportado: '{manifest.Schema}'.");
        }

        if (manifest.PolicyVersion != NeighborhoodAliasCurationPolicy.Version)
        {
            throw new InvalidOperationException(
                $"Manifesto gerado com a politica v{manifest.PolicyVersion}; a politica atual e v{NeighborhoodAliasCurationPolicy.Version}. Regenere o dry-run.");
        }

        if (!NeighborhoodCurationManifestHasher.HashMatches(manifest))
        {
            throw new InvalidOperationException("Hash do manifesto nao confere; gere novamente o dry-run.");
        }

        foreach (var decision in manifest.Decisions)
        {
            var outcome = NeighborhoodCurationOutcomeCodes.ToOutcome(decision.Decision);
            if (decision.Applyable != outcome.IsApplyable())
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': 'applyable' incoerente com a decisao {decision.Decision}.");
            }

            if (outcome == NeighborhoodCurationOutcome.ApprovedByReview && decision.Review is null)
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': aprovacao humana sem dados do revisor.");
            }

            if (decision.UpdateCanonicalName
                && !string.Equals(
                    NeighborhoodName.Normalize(decision.CanonicalName),
                    NeighborhoodName.Normalize(decision.RecommendedCanonicalName),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Grupo '{decision.GroupKey}': correcao de nome canonico nao e apenas acentos/caixa.");
            }
        }

        return (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime().UtcDateTime;
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        // InMemory (unit tests) does not support transactions; relational providers get a real one.
        return _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
    }

    private static async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static string BuildAuditPrefix(string manifestHash)
        => AuditPrefix + ShortHash(manifestHash) + ";";

    private static string ShortHash(string manifestHash)
        => manifestHash.Length <= 12 ? manifestHash : manifestHash[..12];

    private static string BuildAuditNotes(
        NeighborhoodCurationManifest manifest,
        NeighborhoodCurationDecision decision,
        DateTime appliedAtUtc)
    {
        var builder = new StringBuilder();
        builder.Append(AuditPrefix).Append(ShortHash(manifest.ManifestHash));
        builder.Append(";group:").Append(decision.GroupKey);
        builder.Append(";source:")
            .Append(decision.Decision == NeighborhoodCurationOutcomeCodes.ApprovedByReview ? "review" : "auto");

        if (decision.Review is not null)
        {
            builder.Append(";reviewer:").Append(decision.Review.Reviewer);
            builder.Append(";reviewed:").Append(decision.Review.ReviewedAtUtc);
        }

        builder.Append(";applied:").Append(FormatUtc(appliedAtUtc));

        if (decision.Review is not null && decision.Review.Note.Length > 0)
        {
            var remaining = ApprovalNotesMaxLength - builder.Length - ";note:".Length;
            if (remaining > 4)
            {
                var note = decision.Review.Note;
                builder.Append(";note:").Append(note.Length <= remaining ? note : note[..remaining]);
            }
        }

        var notes = builder.ToString();
        return notes.Length <= ApprovalNotesMaxLength ? notes : notes[..ApprovalNotesMaxLength];
    }

    private static string BuildRollbackNotes(string manifestHash, string groupKey, DateTime rolledBackAtUtc)
    {
        return $"{RollbackPrefix}{ShortHash(manifestHash)};group:{groupKey};rolled-back:{FormatUtc(rolledBackAtUtc)}";
    }

    private static string FormatUtc(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
