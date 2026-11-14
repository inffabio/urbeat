using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Urbeat.Infrastructure.Persistence.Migrations;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Guards the SQL shipped by <see cref="AddCnefeGeographicBase"/>. The additive backfill uses
/// chained CTEs; a CTE column reference is only valid when the reading CTE also selects that CTE.
/// PostgreSQL rejects the invalid shape with 42P01 ("missing FROM-clause entry for table ..."),
/// which aborts every WebApi startup with pending migrations, so the guard runs without a server.
/// </summary>
public sealed class CnefeGeographicBaseMigrationTests
{
    private const string StoreDeliveryAreaResolutionAnchor = "SET \"NeighborhoodId\" = r.neighborhood_id";

    private readonly AddCnefeGeographicBase _migration = new();

    private IReadOnlyList<SqlOperation> UpSqlOperations => _migration.UpOperations
        .OfType<SqlOperation>()
        .ToList();

    [Fact]
    public void Up_scripts_should_keep_every_cte_reference_in_scope()
    {
        var violations = UpSqlOperations
            .SelectMany(operation => CteScopeInspector.FindOutOfScopeReferences(operation.Sql))
            .ToList();

        violations.Should().BeEmpty(
            "every qualified CTE reference must live in a scope that selects the CTE; otherwise "
            + "PostgreSQL fails the migration with 42P01 and the API never starts");
    }

    [Fact]
    public void Store_delivery_area_resolution_should_read_accent_map_from_its_own_from_clause()
    {
        var resolutionSql = UpSqlOperations
            .Select(operation => operation.Sql)
            .Single(sql => sql.Contains(StoreDeliveryAreaResolutionAnchor, StringComparison.Ordinal));

        var resolvedCte = CteScopeInspector.FindCte(resolutionSql, "resolved");

        resolvedCte.Should().NotBeNull("the identifier backfill must stay driven by the resolved CTE");
        resolvedCte!.Body.Should().Contain("accent_map.source");
        resolvedCte.Body.Should().Contain(
            "CROSS JOIN accent_map",
            "accent_map is a single-row CTE, so a cross join preserves the original cardinality");
    }

    [Fact]
    public void Up_scripts_should_stay_transactional_so_a_failed_startup_rolls_back_for_a_retry()
    {
        // Program.cs applies migrations with Database.MigrateAsync(): a failing statement must roll
        // the whole migration back (no partial schema, no __EFMigrationsHistory row) so the next
        // container start replays it from scratch.
        UpSqlOperations.Should().OnlyContain(
            operation => !operation.SuppressTransaction,
            "any non-transactional statement would leave the schema half migrated after a failure");
    }

    [Fact]
    public void Up_scripts_should_guard_every_backfill_so_a_replay_is_idempotent()
    {
        var sql = string.Join("\n", UpSqlOperations.Select(operation => operation.Sql));

        // City normalization only fills empty values and delivery areas only resolve pending rows.
        sql.Should().Contain("WHERE c.\"NormalizedName\" = ''");
        sql.Should().Contain("WHERE a.\"NeighborhoodId\" IS NULL");

        // Quarantining an unresolved area is guarded per area id, so a replay cannot duplicate it.
        var issueInsertSql = UpSqlOperations
            .Select(operation => operation.Sql)
            .Single(statement => statement.Contains(
                "INSERT INTO \"StoreDeliveryAreaMigrationIssues\"",
                StringComparison.Ordinal));

        issueInsertSql.Should().Contain("NOT EXISTS");
        issueInsertSql.Should().Contain("SELECT 1 FROM \"StoreDeliveryAreaMigrationIssues\" i");
    }

    private sealed record CteDefinition(string Name, int BodyEnd, string Body);

    /// <summary>
    /// Minimal SQL reader: extracts CTE bodies and flags CTE-qualified references made from a
    /// scope (another CTE or the main statement) whose FROM/JOIN list omits that CTE.
    /// </summary>
    private static class CteScopeInspector
    {
        private static readonly Regex CteHeader = new(
            @"(?<name>[a-z_][a-z0-9_]*)\s+AS\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<string> FindOutOfScopeReferences(string sql)
        {
            var sanitized = StripStringLiterals(sql);
            var ctes = ExtractCtes(sanitized);
            if (ctes.Count == 0)
            {
                return Array.Empty<string>();
            }

            var scopes = new List<(string Label, string Body)>();
            foreach (var cte in ctes)
            {
                scopes.Add((cte.Name, cte.Body));
            }

            scopes.Add(("main statement", ExtractMainStatement(sanitized, ctes)));

            var violations = new List<string>();
            for (var cteIndex = 0; cteIndex < ctes.Count; cteIndex++)
            {
                var cte = ctes[cteIndex];
                for (var scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++)
                {
                    if (scopeIndex == cteIndex)
                    {
                        continue;
                    }

                    var scope = scopes[scopeIndex];
                    if (HasQualifiedReference(scope.Body, cte.Name)
                        && !HasBareReference(scope.Body, cte.Name))
                    {
                        violations.Add(
                            $"CTE '{cte.Name}' is referenced with a qualified column inside '{scope.Label}' "
                            + "without being selected in that scope's FROM/JOIN list");
                    }
                }
            }

            return violations;
        }

        public static CteDefinition? FindCte(string sql, string name) =>
            ExtractCtes(StripStringLiterals(sql))
                .FirstOrDefault(cte => string.Equals(cte.Name, name, StringComparison.OrdinalIgnoreCase));

        private static List<CteDefinition> ExtractCtes(string sanitized)
        {
            var ctes = new List<CteDefinition>();
            var depths = ComputeDepths(sanitized);

            foreach (Match match in CteHeader.Matches(sanitized))
            {
                // Only CTE headers sit at the outer nesting level; the body itself follows the '('.
                if (depths[match.Index] != 0)
                {
                    continue;
                }

                var open = match.Index + match.Length - 1;
                if (open < 0 || open >= sanitized.Length || sanitized[open] != '(')
                {
                    continue;
                }

                var close = FindClosingParenthesis(sanitized, open);
                if (close < 0)
                {
                    continue;
                }

                ctes.Add(new CteDefinition(
                    match.Groups["name"].Value,
                    close,
                    sanitized[(open + 1)..close]));
            }

            return ctes;
        }

        private static string ExtractMainStatement(string sanitized, List<CteDefinition> ctes)
        {
            if (ctes.Count == 0)
            {
                return sanitized;
            }

            var end = ctes.Max(cte => cte.BodyEnd);
            return end + 1 >= sanitized.Length ? string.Empty : sanitized[(end + 1)..];
        }

        /// <summary>Number of unclosed '(' before each character.</summary>
        private static int[] ComputeDepths(string sanitized)
        {
            var depths = new int[sanitized.Length];
            var depth = 0;

            for (var i = 0; i < sanitized.Length; i++)
            {
                depths[i] = depth;

                if (sanitized[i] == '(')
                {
                    depth++;
                }
                else if (sanitized[i] == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }
            }

            return depths;
        }

        private static int FindClosingParenthesis(string sanitized, int open)
        {
            var depth = 0;
            for (var i = open; i < sanitized.Length; i++)
            {
                if (sanitized[i] == '(')
                {
                    depth++;
                }
                else if (sanitized[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static bool HasQualifiedReference(string text, string name) =>
            Regex.IsMatch(
                text,
                $@"(?<![\w""]){Regex.Escape(name)}\.\w",
                RegexOptions.IgnoreCase);

        private static bool HasBareReference(string text, string name) =>
            Regex.IsMatch(
                text,
                $@"(?<![\w""]){Regex.Escape(name)}(?![\w"".])",
                RegexOptions.IgnoreCase);

        private static string StripStringLiterals(string sql)
        {
            var builder = new StringBuilder(sql.Length);
            for (var i = 0; i < sql.Length; i++)
            {
                if (sql[i] != '\'')
                {
                    builder.Append(sql[i]);
                    continue;
                }

                builder.Append('\'');
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i += 2;
                        continue;
                    }

                    if (sql[i] == '\'')
                    {
                        break;
                    }

                    i++;
                }

                builder.Append('\'');
            }

            return builder.ToString();
        }
    }
}
