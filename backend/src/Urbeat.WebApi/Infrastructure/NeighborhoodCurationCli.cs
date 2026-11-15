using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Urbeat.Application.Geo.Curation;
using Urbeat.Infrastructure.Curation;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.WebApi.Infrastructure;

/// <summary>
/// Explicit, opt-in curation command. It is never reached by the web host: the process must be
/// started with <c>--curation</c>, which keeps the operation auditable and impossible to trigger
/// from an HTTP request. Secrets are read from the same environment as the API and are never logged.
/// </summary>
public static class NeighborhoodCurationCli
{
    public const string Switch = "--curation";

    private static readonly JsonSerializerOptions JsonOptions = NeighborhoodCurationManifestHasher.JsonOptions;

    public static bool IsRequested(string[] args)
    {
        return args.Any(argument =>
            string.Equals(argument, Switch, StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith(Switch + "=", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> RunAsync(WebApplication app, string[] args, CancellationToken cancellationToken = default)
    {
        var command = ReadCommand(args);
        if (command is null)
        {
            Console.Error.WriteLine(
                "Uso: --curation <build-manifest|dry-run|apply|rollback|verify|backup-sql> [opcoes]\n" +
                "Write commands (apply/rollback) exigem --manifest <arquivo> --confirm-hash <manifest_hash>.");
            return 2;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var curator = scope.ServiceProvider.GetRequiredService<NeighborhoodAliasCurator>();
        var service = scope.ServiceProvider.GetRequiredService<NeighborhoodAliasApplyService>();

        try
        {
            return command switch
            {
                "build-manifest" => await BuildManifestAsync(args, curator, cancellationToken),
                "dry-run" => await DryRunAsync(args, curator, service, cancellationToken),
                "apply" => await ApplyAsync(args, service, cancellationToken),
                "rollback" => await RollbackAsync(args, service, cancellationToken),
                "verify" => await VerifyAsync(args, db, service, cancellationToken),
                "backup-sql" => await BackupSqlAsync(args, cancellationToken),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Falha na curadoria ({command}): {exception.Message}");
            return 1;
        }
    }

    private static string? ReadCommand(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, Switch, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[index + 1].Trim().ToLowerInvariant()
                    : null;
            }

            if (argument.StartsWith(Switch + "=", StringComparison.OrdinalIgnoreCase))
            {
                return argument[(Switch.Length + 1)..].Trim().ToLowerInvariant();
            }
        }

        return null;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Comando de curadoria desconhecido: '{command}'.");
        return 2;
    }

    private static async Task<int> BuildManifestAsync(
        string[] args,
        NeighborhoodAliasCurator curator,
        CancellationToken cancellationToken)
    {
        var (manifest, manifestPath, reportPath) = await BuildManifestFromFilesAsync(
            args,
            curator,
            cancellationToken);

        await File.WriteAllTextAsync(
            manifestPath,
            NeighborhoodCurationManifestHasher.Serialize(manifest),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        var report = new
        {
            command = "build-manifest",
            manifest_hash = manifest.ManifestHash,
            manifest_path = manifestPath,
            queue_sha256 = manifest.Inputs.QueueSha256,
            decisions_sha256 = manifest.Inputs.DecisionsSha256,
            totals = manifest.Totals
        };

        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return 0;
    }

    private static async Task<(NeighborhoodCurationManifest Manifest, string ManifestPath, string ReportPath)>
        BuildManifestFromFilesAsync(
            string[] args,
            NeighborhoodAliasCurator curator,
            CancellationToken cancellationToken)
    {
        var queuePath = Required(args, "--queue");
        var manifestPath = Required(args, "--manifest-out");
        var decisionsPath = Optional(args, "--decisions");
        var repairsPath = Optional(args, "--repairs");
        var reportPath = Optional(args, "--report-out")
            ?? manifestPath + ".report.json";

        var queueBytes = await File.ReadAllBytesAsync(queuePath, cancellationToken);
        var queue = Deserialize<NeighborhoodCurationQueueDocument>(queueBytes, queuePath);

        var decisionsBytes = decisionsPath is null
            ? null
            : await File.ReadAllBytesAsync(decisionsPath, cancellationToken);
        var decisions = decisionsBytes is null
            ? null
            : Deserialize<NeighborhoodCurationDecisionsDocument>(decisionsBytes, decisionsPath!);

        var repairsBytes = repairsPath is null
            ? null
            : await File.ReadAllBytesAsync(repairsPath, cancellationToken);
        var repairs = repairsBytes is null
            ? null
            : Deserialize<NeighborhoodCurationRepairsDocument>(repairsBytes, repairsPath!);

        var manifest = curator.BuildManifest(
            queue,
            queuePath,
            NeighborhoodCurationManifestHasher.ComputeFileHash(queueBytes),
            approvals: null,
            approvalsPath: null,
            approvalsSha256: null,
            generatedAtUtc: DateTimeOffset.UtcNow,
            explicitDecisions: decisions,
            repairsDocument: repairs,
            decisionsPath: decisionsPath,
            decisionsSha256: decisionsBytes is null
                ? null
                : NeighborhoodCurationManifestHasher.ComputeFileHash(decisionsBytes));

        return (manifest, manifestPath, reportPath);
    }

    private static async Task<int> DryRunAsync(
        string[] args,
        NeighborhoodAliasCurator curator,
        NeighborhoodAliasApplyService service,
        CancellationToken cancellationToken)
    {
        var (manifest, manifestPath, reportPath) = await BuildManifestFromFilesAsync(
            args,
            curator,
            cancellationToken);

        var planned = await service.DryRunAsync(manifest, cancellationToken: cancellationToken);

        await File.WriteAllTextAsync(
            manifestPath,
            NeighborhoodCurationManifestHasher.Serialize(manifest),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        var report = new
        {
            command = "dry-run",
            manifest_hash = manifest.ManifestHash,
            manifest_path = manifestPath,
            queue_sha256 = manifest.Inputs.QueueSha256,
            decisions_sha256 = manifest.Inputs.DecisionsSha256,
            totals = manifest.Totals,
            planned = new
            {
                aliases_inserted = planned.AliasesInserted,
                aliases_reactivated = planned.AliasesReactivated,
                aliases_unchanged = planned.AliasesUnchanged,
                display_names_set = planned.DisplayNamesSet,
                canonical_names_updated = planned.CanonicalNamesUpdated,
                store_areas_remapped = planned.StoreAreasRemapped,
                store_areas_removed_as_duplicate = planned.StoreAreasRemovedAsDuplicate,
                repairs_applied = planned.RepairsApplied
            },
            warnings = planned.Warnings
        };

        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return 0;
    }

    private static async Task<int> ApplyAsync(
        string[] args,
        NeighborhoodAliasApplyService service,
        CancellationToken cancellationToken)
    {
        var manifestPath = Required(args, "--manifest");
        // The confirmation is mandatory: writing aliases requires echoing the exact manifest hash
        // produced by the dry-run, so an unattended apply cannot happen by accident.
        var expectedHash = Required(args, "--confirm-hash");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
        ValidateConfirmation(manifest, expectedHash, "apply");

        var result = await service.ApplyAsync(manifest, cancellationToken: cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            command = "apply",
            manifest_hash = manifest.ManifestHash,
            applied_at_utc = result.AppliedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            aliases_inserted = result.AliasesInserted,
            aliases_reactivated = result.AliasesReactivated,
            aliases_unchanged = result.AliasesUnchanged,
            display_names_set = result.DisplayNamesSet,
            canonical_names_updated = result.CanonicalNamesUpdated,
            store_areas_remapped = result.StoreAreasRemapped,
            store_areas_removed_as_duplicate = result.StoreAreasRemovedAsDuplicate,
            repairs_applied = result.RepairsApplied,
            warnings = result.Warnings
        }, JsonOptions));
        return 0;
    }

    private static async Task<int> RollbackAsync(
        string[] args,
        NeighborhoodAliasApplyService service,
        CancellationToken cancellationToken)
    {
        var manifestPath = Required(args, "--manifest");
        var expectedHash = Required(args, "--confirm-hash");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
        ValidateConfirmation(manifest, expectedHash, "rollback");

        var result = await service.RollbackAsync(manifest, cancellationToken: cancellationToken);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            command = "rollback",
            manifest_hash = manifest.ManifestHash,
            aliases_deactivated = result.AliasesDeactivated,
            display_names_cleared = result.DisplayNamesCleared,
            canonical_names_restored = result.CanonicalNamesRestored,
            warnings = result.Warnings
        }, JsonOptions));
        return 0;
    }

    private static async Task<int> VerifyAsync(
        string[] args,
        ApplicationDbContext db,
        NeighborhoodAliasApplyService service,
        CancellationToken cancellationToken)
    {
        var manifestPath = Required(args, "--manifest");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
        var dryRun = await service.DryRunAsync(manifest, cancellationToken: cancellationToken);

        var aliasIds = manifest.ApplyableDecisions
            .SelectMany(decision => decision.Aliases)
            .Select(alias => Guid.TryParse(alias.AliasId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        var canonicalIds = manifest.ApplyableDecisions
            .Select(decision => Guid.TryParse(decision.CanonicalNeighborhoodId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        var aliasRows = await db.DeliveryNeighborhoodAliases
            .AsNoTracking()
            .Where(alias => aliasIds.Contains(alias.Id))
            .Select(alias => new { alias.Id, alias.IsActive, alias.IsApproved, alias.CanonicalNeighborhoodId })
            .ToListAsync(cancellationToken);

        var activeAliases = aliasRows.Count(alias => alias.IsActive && alias.IsApproved);
        var displayNames = await db.DeliveryNeighborhoods
            .AsNoTracking()
            .CountAsync(neighborhood => canonicalIds.Contains(neighborhood.Id) && neighborhood.DisplayName != null, cancellationToken);
        var canonicalNames = await db.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(neighborhood => canonicalIds.Contains(neighborhood.Id))
            .Select(neighborhood => neighborhood.Neighborhood)
            .ToListAsync(cancellationToken);
        var accentsPreserved = manifest.ApplyableDecisions.Count(decision =>
            decision.UpdateCanonicalName
            && canonicalNames.Contains(decision.RecommendedCanonicalName));

        var report = new
        {
            command = "verify",
            manifest_hash = manifest.ManifestHash,
            expected = new
            {
                applyable_groups = manifest.Totals.Applyable,
                applyable_aliases = manifest.Totals.ApplyableAliases,
                repairs = manifest.Totals.Repairs
            },
            actual = new
            {
                active_aliases = activeAliases,
                alias_rows_found = aliasRows.Count,
                display_names = displayNames,
                canonical_names_with_accents = accentsPreserved,
                store_areas_to_remap = dryRun.StoreAreasRemapped,
                store_area_duplicates = dryRun.StoreAreasRemovedAsDuplicate,
                repairs_pending = dryRun.RepairsApplied
            },
            warnings = dryRun.Warnings
        };

        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return 0;
    }

    private static async Task<int> BackupSqlAsync(string[] args, CancellationToken cancellationToken)
    {
        var manifestPath = Required(args, "--manifest");
        var outputPath = Required(args, "--out");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken);

        var neighborhoodIds = new HashSet<Guid>();
        foreach (var decision in manifest.ApplyableDecisions)
        {
            if (Guid.TryParse(decision.CanonicalNeighborhoodId, out var canonicalId))
            {
                neighborhoodIds.Add(canonicalId);
            }

            foreach (var alias in decision.Aliases)
            {
                if (Guid.TryParse(alias.NeighborhoodId, out var aliasId))
                {
                    neighborhoodIds.Add(aliasId);
                }
            }
        }

        foreach (var repair in manifest.Repairs)
        {
            if (Guid.TryParse(repair.NeighborhoodId, out var repairId))
            {
                neighborhoodIds.Add(repairId);
            }
        }

        var cityIds = manifest.ApplyableDecisions
            .Select(decision => decision.CityId)
            .Where(cityId => !string.IsNullOrWhiteSpace(cityId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(cityId => cityId!.Trim())
            .Order(StringComparer.Ordinal)
            .ToList();

        var idList = string.Join(",", neighborhoodIds.Select(id => $"'{id}'"));
        var cityList = string.Join(",", cityIds.Select(id => $"'{id}'"));

        var sql = new StringBuilder();
        sql.AppendLine("\\set ON_ERROR_STOP on");
        sql.AppendLine($"\\copy (SELECT * FROM \"DeliveryNeighborhoods\" WHERE \"Id\" IN ({idList})) TO '/tmp/urbeat-neighborhood-backup.csv' WITH (FORMAT csv, HEADER true)");
        sql.AppendLine($"\\copy (SELECT * FROM \"StoreDeliveryAreas\" WHERE \"NeighborhoodId\" IN ({idList})) TO '/tmp/urbeat-store-area-backup.csv' WITH (FORMAT csv, HEADER true)");
        sql.AppendLine($"\\copy (SELECT * FROM \"StoreDeliveryAreas\" WHERE \"StoreId\" IN (SELECT DISTINCT \"StoreId\" FROM \"StoreDeliveryAreas\" WHERE \"NeighborhoodId\" IN ({idList}))) TO '/tmp/urbeat-store-area-by-store-backup.csv' WITH (FORMAT csv, HEADER true)");
        sql.AppendLine($"\\copy (SELECT * FROM \"DeliveryNeighborhoodAliases\" WHERE \"CityId\" IN ({cityList}) OR \"CanonicalNeighborhoodId\" IN ({idList})) TO '/tmp/urbeat-neighborhood-alias-backup.csv' WITH (FORMAT csv, HEADER true)");
        sql.AppendLine($"\\copy (SELECT * FROM \"AuditLogs\" WHERE \"EntityId\" IN ({idList})) TO '/tmp/urbeat-audit-backup.csv' WITH (FORMAT csv, HEADER true)");

        await File.WriteAllTextAsync(outputPath, sql.ToString(), cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            command = "backup-sql",
            manifest_hash = manifest.ManifestHash,
            output = outputPath,
            neighborhoods = neighborhoodIds.Count,
            cities = cityIds.Count
        }, JsonOptions));
        return 0;
    }

    private static async Task<NeighborhoodCurationManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return NeighborhoodCurationManifestHasher.Deserialize(json);
    }

    private static T Deserialize<T>(byte[] content, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new InvalidOperationException($"Arquivo vazio: {path}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"JSON invalido em {path}: {exception.Message}");
        }
    }

    /// <summary>
    /// Mandatory guard for write commands. The manifest must be self-consistent and the operator must
    /// echo the exact hash produced by the dry-run; an unattended apply/rollback is refused.
    /// </summary>
    public static void ValidateConfirmation(
        NeighborhoodCurationManifest manifest,
        string? expectedHash,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!NeighborhoodCurationManifestHasher.HashMatches(manifest))
        {
            throw new InvalidOperationException($"Hash do manifesto nao confere; {operation} recusado.");
        }

        if (string.IsNullOrWhiteSpace(expectedHash)
            || !string.Equals(expectedHash.Trim(), manifest.ManifestHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"--confirm-hash nao corresponde ao manifesto; {operation} recusado.");
        }
    }

    private static string Required(string[] args, string name)
    {
        return Optional(args, name)
            ?? throw new InvalidOperationException($"Opcao obrigatoria ausente: {name}");
    }

    private static string? Optional(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[index + 1];
            }

            if (args[index].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[index][(name.Length + 1)..];
            }
        }

        return null;
    }
}
