using System.Globalization;
using Urbeat.Domain.Geo;
namespace Urbeat.Application.Geo.Curation;

/// <summary>
/// Turns a read-only curation queue plus optional reviewer approvals into a content-addressed
/// manifest. It never touches the database: the manifest is the only output, which is what makes the
/// dry-run reproducible and safe to run nationally.
/// </summary>
public sealed class NeighborhoodAliasCurator
{
    private readonly NeighborhoodAliasCurationPolicy _policy;

    public NeighborhoodAliasCurator(NeighborhoodAliasCurationPolicy? policy = null)
    {
        _policy = policy ?? new NeighborhoodAliasCurationPolicy();
    }

    public NeighborhoodCurationManifest BuildManifest(
        NeighborhoodCurationQueueDocument queue,
        string queuePath,
        string queueSha256,
        NeighborhoodCurationApprovalsDocument? approvals = null,
        string? approvalsPath = null,
        string? approvalsSha256 = null,
        DateTimeOffset? generatedAtUtc = null,
        NeighborhoodCurationDecisionsDocument? explicitDecisions = null,
        NeighborhoodCurationRepairsDocument? repairsDocument = null,
        string? decisionsPath = null,
        string? decisionsSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (string.IsNullOrWhiteSpace(queueSha256))
        {
            throw new InvalidOperationException("O hash da fila de curadoria e obrigatorio.");
        }

        var groups = queue.Groups ?? [];
        if (groups.Count == 0)
        {
            throw new InvalidOperationException("A fila de curadoria nao tem grupos.");
        }

        var groupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var key = group.Grupo?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Grupo de curadoria sem identificador ('grupo').");
            }

            if (!groupKeys.Add(key))
            {
                throw new InvalidOperationException($"Grupo '{key}' duplicado na fila de curadoria.");
            }
        }

        var approvalsByGroup = IndexApprovals(approvals, groupKeys);
        var explicitByGroup = IndexExplicitDecisions(explicitDecisions, groupKeys);

        var decisions = new List<NeighborhoodCurationDecision>(groups.Count);
        foreach (var group in groups)
        {
            var key = group.Grupo!.Trim();
            var assessment = _policy.Assess(group);

            NeighborhoodCurationReview? review = null;
            var outcome = assessment.Outcome;

            if (explicitByGroup.TryGetValue(key, out var explicitDecision))
            {
                (outcome, assessment) = ApplyExplicitDecision(key, group, assessment, explicitDecision);
                review = BuildReview(key, explicitDecision);
            }
            else if (approvalsByGroup.TryGetValue(key, out var approval))
            {
                review = ValidateApproval(key, approval, assessment);
                outcome = NeighborhoodCurationOutcome.ApprovedByReview;
            }

            decisions.Add(BuildDecision(group, key, assessment, outcome, review, explicitDecision));
        }

        decisions = ResolveCrossGroupConflicts(decisions);

        var repairs = BuildRepairs(repairsDocument);

        var manifestHash = NeighborhoodCurationManifestHasher.ComputeDecisionHash(
            NeighborhoodAliasCurationPolicy.Version,
            queueSha256,
            approvalsSha256,
            decisions,
            repairs);

        decisions = AssignAliasIds(decisions, manifestHash).ToList();

        var totals = new NeighborhoodCurationManifestTotals
        {
            Groups = decisions.Count,
            AutoApprove = decisions.Count(x => x.Decision == NeighborhoodCurationOutcomeCodes.AutoApprove),
            HumanReview = decisions.Count(x => x.Decision == NeighborhoodCurationOutcomeCodes.HumanReview),
            ApprovedByReview = decisions.Count(x => x.Decision == NeighborhoodCurationOutcomeCodes.ApprovedByReview),
            Blocked = decisions.Count(x => x.Decision == NeighborhoodCurationOutcomeCodes.Blocked),
            KeepSeparate = decisions.Count(x => x.Decision == NeighborhoodCurationOutcomeCodes.KeepSeparate),
            Applyable = decisions.Count(x => x.Applyable),
            ApplyableAliases = decisions.Where(x => x.Applyable).Sum(x => x.Aliases.Count),
            Repairs = repairs.Count
        };

        return new NeighborhoodCurationManifest
        {
            Schema = NeighborhoodCurationManifestSchema.Current,
            PolicyVersion = NeighborhoodAliasCurationPolicy.Version,
            GeneratedAtUtc = (generatedAtUtc ?? DateTimeOffset.UtcNow)
                .ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            DryRun = true,
            ManifestHash = manifestHash,
            Inputs = new NeighborhoodCurationManifestInputs
            {
                QueuePath = queuePath,
                QueueSha256 = queueSha256,
                QueueSchema = queue.Schema,
                ApprovalsPath = approvalsPath,
                ApprovalsSha256 = approvalsSha256,
                DecisionsPath = decisionsPath,
                DecisionsSha256 = decisionsSha256
            },
            Totals = totals,
            Decisions = decisions,
            Repairs = repairs
        };
    }

    /// <summary>
    /// Detects two different groups in the same city claiming the same normalized alias for
    /// different canonicals. Such ambiguity can never be applied and is blocked before hashing.
    /// </summary>
    private static List<NeighborhoodCurationDecision> ResolveCrossGroupConflicts(
        List<NeighborhoodCurationDecision> decisions)
    {
        var conflicting = decisions
            .Where(decision => decision.Applyable)
            .SelectMany(decision => decision.Aliases.Select(alias => new
            {
                decision.GroupKey,
                decision.CityId,
                alias.NormalizedName,
                Canonical = decision.CanonicalNeighborhoodId
            }))
            .GroupBy(entry => $"{entry.CityId}|{entry.NormalizedName}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(entry => entry.Canonical).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .SelectMany(group => group.Select(entry => entry.GroupKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (conflicting.Count == 0)
        {
            return decisions;
        }

        var explicitConflict = decisions.Any(decision =>
            conflicting.Contains(decision.GroupKey)
            && decision.Decision == NeighborhoodCurationOutcomeCodes.ApprovedByReview);

        if (explicitConflict)
        {
            throw new InvalidOperationException(
                "Duas decisoes explicitas da mesma cidade reivindicam o mesmo alias normalizado para canonicos diferentes: " +
                string.Join(", ", conflicting.Order(StringComparer.Ordinal)) + ".");
        }

        return decisions.Select(decision =>
        {
            if (!decision.Applyable || !conflicting.Contains(decision.GroupKey))
            {
                return decision;
            }

            var reasons = decision.Reasons
                .Append(NeighborhoodCurationReason.Block(
                    NeighborhoodCurationReasonCodes.AliasConflictAcrossGroups,
                    "Mais de um grupo da mesma cidade reivindica o mesmo alias normalizado para canonicos diferentes."))
                .ToList();

            return decision with
            {
                Decision = NeighborhoodCurationOutcomeCodes.Blocked,
                Applyable = false,
                Reasons = reasons,
                Review = null
            };
        }).ToList();
    }

    private static IEnumerable<NeighborhoodCurationDecision> AssignAliasIds(
        IEnumerable<NeighborhoodCurationDecision> decisions,
        string manifestHash)
    {
        foreach (var decision in decisions)
        {
            if (!decision.Applyable || decision.Aliases.Count == 0 || !Guid.TryParse(decision.CityId, out var cityId))
            {
                yield return decision;
                continue;
            }

            yield return decision with
            {
                Aliases = decision.Aliases
                    .Select(alias => alias with
                    {
                        AliasId = NeighborhoodCurationManifestHasher
                            .AliasIdFor(manifestHash, cityId, alias.NormalizedName)
                            .ToString()
                    })
                    .ToList()
            };
        }
    }

    private static NeighborhoodCurationDecision BuildDecision(
        NeighborhoodCurationQueueGroup group,
        string key,
        NeighborhoodCurationAssessment assessment,
        NeighborhoodCurationOutcome outcome,
        NeighborhoodCurationReview? review,
        NeighborhoodCurationExplicitDecision? explicitDecision)
    {
        var aliases = outcome.IsApplyable()
            ? assessment.Aliases
                .Select(alias => new NeighborhoodCurationAliasDecision
                {
                    NeighborhoodId = alias.NeighborhoodId.ToString(),
                    Name = alias.Name,
                    NormalizedName = alias.NormalizedName
                })
                .ToList()
            : new List<NeighborhoodCurationAliasDecision>();

        var updateCanonicalName = explicitDecision is not null
            && outcome.IsApplyable()
            && !string.Equals(
                assessment.RecommendedCanonicalName,
                assessment.CanonicalName,
                StringComparison.Ordinal);

        return new NeighborhoodCurationDecision
        {
            GroupKey = key,
            PriorityRank = group.PriorityRank,
            Uf = group.Uf?.Trim().ToUpperInvariant() ?? string.Empty,
            Ibge = group.Ibge?.Trim() ?? string.Empty,
            City = group.Cidade?.Trim() ?? string.Empty,
            CityId = assessment.CityId?.ToString(),
            Decision = outcome.ToCode(),
            Applyable = outcome.IsApplyable(),
            Reasons = assessment.Reasons,
            CanonicalNeighborhoodId = assessment.CanonicalNeighborhoodId?.ToString(),
            CanonicalName = assessment.CanonicalName,
            RecommendedCanonicalName = assessment.RecommendedCanonicalName,
            UpdateCanonicalName = updateCanonicalName,
            DisplayName = assessment.DisplayName,
            Aliases = aliases,
            DistanceMaxKm = FormatDistance(assessment.DistanceMaxKm),
            DistanceMinKm = FormatDistance(assessment.DistanceMinKm),
            Evidence = !string.IsNullOrWhiteSpace(explicitDecision?.Evidence)
                ? explicitDecision!.Evidence!.Trim()
                : string.IsNullOrWhiteSpace(group.Evidence) ? null : group.Evidence.Trim(),
            Review = review
        };
    }

    private static Dictionary<string, NeighborhoodCurationExplicitDecision> IndexExplicitDecisions(
        NeighborhoodCurationDecisionsDocument? document,
        HashSet<string> queueGroupKeys)
    {
        var result = new Dictionary<string, NeighborhoodCurationExplicitDecision>(StringComparer.OrdinalIgnoreCase);
        if (document is null || document.Decisions is null)
        {
            return result;
        }

        if (!string.Equals(document.Schema, NeighborhoodCurationDecisionsSchema.Current, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Schema de decisoes explicito nao suportado: '{document.Schema}'.");
        }

        foreach (var decision in document.Decisions)
        {
            var key = decision.GroupKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Decisao explicita sem 'group_key'.");
            }

            if (!queueGroupKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    $"Decisao explicita para o grupo '{key}', que nao existe na fila de curadoria.");
            }

            var action = decision.Action?.Trim().ToLowerInvariant();
            if (action is not ("merge" or "keep_separate"))
            {
                throw new InvalidOperationException(
                    $"Grupo '{key}': acao explicita invalida ('{decision.Action}'); use merge ou keep_separate.");
            }

            if (!result.TryAdd(key, decision))
            {
                throw new InvalidOperationException($"Decisao explicita duplicada para o grupo '{key}'.");
            }
        }

        return result;
    }

    private static (NeighborhoodCurationOutcome Outcome, NeighborhoodCurationAssessment Assessment) ApplyExplicitDecision(
        string groupKey,
        NeighborhoodCurationQueueGroup group,
        NeighborhoodCurationAssessment assessment,
        NeighborhoodCurationExplicitDecision decision)
    {
        var action = decision.Action.Trim().ToLowerInvariant();
        var members = group.Members ?? [];

        if (action == "keep_separate")
        {
            var canonicalMember = ResolveMember(members, decision.CanonicalNeighborhoodId)
                ?? members.FirstOrDefault(member => member.IsUpper)
                ?? members.FirstOrDefault();

            var canonicalName = canonicalMember?.Name?.Trim()
                ?? group.CanonicalName?.Trim()
                ?? string.Empty;

            return (
                NeighborhoodCurationOutcome.KeepSeparate,
                assessment with
                {
                    Outcome = NeighborhoodCurationOutcome.KeepSeparate,
                    CanonicalNeighborhoodId = canonicalMember?.Id is { } id && Guid.TryParse(id, out var parsed)
                        ? parsed
                        : assessment.CanonicalNeighborhoodId,
                    CanonicalName = canonicalName,
                    RecommendedCanonicalName = canonicalName,
                    DisplayName = NeighborhoodName.ToUppercaseDisplay(
                        canonicalName,
                        NeighborhoodAliasCurationPolicy.MaxDisplayNameLength),
                    Aliases = [],
                    Reasons = assessment.Reasons
                        .Append(NeighborhoodCurationReason.Info(
                            NeighborhoodCurationReasonCodes.KeptSeparateByDecision,
                            "Curador decidiu manter os nomes como bairros distintos; nenhum alias sera criado."))
                        .ToList()
                });
        }

        var canonical = ResolveMember(members, decision.CanonicalNeighborhoodId)
            ?? throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'canonical_neighborhood_id' explicito nao pertence ao grupo.");

        if (!Guid.TryParse(canonical.Id, out var canonicalId))
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': identificador do canonico explicito invalido.");
        }

        var canonicalCurrentName = canonical.Name?.Trim() ?? string.Empty;
        var desiredName = string.IsNullOrWhiteSpace(decision.CanonicalName)
            ? canonicalCurrentName
            : decision.CanonicalName.Trim();

        if (desiredName.Length == 0)
        {
            throw new InvalidOperationException($"Grupo '{groupKey}': nome canonico explicito vazio.");
        }

        if (!string.Equals(
                NeighborhoodName.Normalize(desiredName),
                NeighborhoodName.Normalize(canonicalCurrentName),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': o nome canonico explicito so pode corrigir acentos/caixa; " +
                $"'{desiredName}' muda letras, ordem ou plural de '{canonicalCurrentName}'.");
        }

        var requestedAliasIds = decision.Aliases
            .Select(alias => alias.NeighborhoodId?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        var aliasMembers = requestedAliasIds.Count == 0
            ? members
                .Where(member => !SameId(member.Id, canonical.Id))
                .ToList()
            : requestedAliasIds
                .Select(id => ResolveMember(members, id)
                    ?? throw new InvalidOperationException(
                        $"Grupo '{groupKey}': alias explicito '{id}' nao pertence ao grupo."))
                .ToList();

        foreach (var aliasMember in aliasMembers)
        {
            if (!Guid.TryParse(aliasMember.Id, out _))
            {
                throw new InvalidOperationException(
                    $"Grupo '{groupKey}': identificador de alias invalido ('{aliasMember.Name}').");
            }

            if (SameId(aliasMember.Id, canonical.Id))
            {
                throw new InvalidOperationException(
                    $"Grupo '{groupKey}': o canonico nao pode ser alias de si mesmo.");
            }

            var requestedName = decision.Aliases
                .FirstOrDefault(alias => SameId(alias.NeighborhoodId, aliasMember.Id))?.Name;

            if (!string.IsNullOrWhiteSpace(requestedName)
                && !string.Equals(
                    NeighborhoodName.Normalize(requestedName),
                    NeighborhoodName.Normalize(aliasMember.Name),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Grupo '{groupKey}': o nome do alias '{requestedName}' nao corresponde ao registro {aliasMember.Id}.");
            }
        }

        var normalizedAliases = aliasMembers
            .Select(member => new NeighborhoodCurationAliasClaim(
                Guid.Parse(member.Id!),
                member.Name?.Trim() ?? string.Empty,
                NeighborhoodName.Normalize(member.Name)))
            .ToList();

        if (normalizedAliases.Any(alias => alias.NormalizedName.Length == 0))
        {
            throw new InvalidOperationException($"Grupo '{groupKey}': alias com nome normalizado vazio.");
        }

        var duplicateAlias = normalizedAliases
            .GroupBy(alias => alias.NormalizedName, StringComparer.Ordinal)
            .FirstOrDefault(grouping => grouping.Count() > 1);

        if (duplicateAlias is not null)
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': dois aliases com o mesmo nome normalizado ('{duplicateAlias.Key}').");
        }

        var reasons = assessment.Reasons
            .Append(NeighborhoodCurationReason.Info(
                NeighborhoodCurationReasonCodes.ExplicitCuratorDecision,
                "Decisao explicita de curadoria registrada neste manifesto."))
            .ToList();

        if (!string.Equals(desiredName, canonicalCurrentName, StringComparison.Ordinal))
        {
            reasons.Add(NeighborhoodCurationReason.Info(
                NeighborhoodCurationReasonCodes.CanonicalNameAccentsRestored,
                $"Nome canonico corrigido de '{canonicalCurrentName}' para '{desiredName}' (somente acentos/caixa)."));
        }

        if (group.StoreAreaCount > 0 || members.Any(member => member.StoreDeliveryAreas > 0))
        {
            reasons.Add(NeighborhoodCurationReason.Info(
                NeighborhoodCurationReasonCodes.StoreAreasRemapped,
                "StoreDeliveryAreas dos aliases serao remapeadas para o canonico preservando taxa, notas e status."));
        }

        return (
            NeighborhoodCurationOutcome.ApprovedByReview,
            assessment with
            {
                Outcome = NeighborhoodCurationOutcome.ApprovedByReview,
                CityId = Guid.TryParse(group.CityId, out var groupCityId) ? groupCityId : assessment.CityId,
                CanonicalNeighborhoodId = canonicalId,
                CanonicalName = canonicalCurrentName,
                RecommendedCanonicalName = desiredName,
                DisplayName = NeighborhoodName.ToUppercaseDisplay(
                    desiredName,
                    NeighborhoodAliasCurationPolicy.MaxDisplayNameLength),
                Aliases = normalizedAliases,
                Reasons = reasons
            });
    }

    private static NeighborhoodCurationQueueMember? ResolveMember(
        IReadOnlyList<NeighborhoodCurationQueueMember> members,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return members.FirstOrDefault(member => SameId(member.Id, id));
    }

    private static bool SameId(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static NeighborhoodCurationReview BuildReview(
        string groupKey,
        NeighborhoodCurationExplicitDecision decision)
    {
        var reviewer = decision.Reviewer?.Trim() ?? string.Empty;
        if (reviewer.Length == 0 || reviewer.Length > NeighborhoodAliasCurationPolicy.MaxReviewerNameLength)
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'reviewer' obrigatorio com ate {NeighborhoodAliasCurationPolicy.MaxReviewerNameLength} caracteres.");
        }

        var note = decision.Note?.Trim() ?? string.Empty;
        if (note.Length == 0 || note.Length > NeighborhoodAliasCurationPolicy.MaxReviewNoteLength)
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'note' obrigatoria com ate {NeighborhoodAliasCurationPolicy.MaxReviewNoteLength} caracteres.");
        }

        string? reviewerUserId = null;
        if (!string.IsNullOrWhiteSpace(decision.ReviewerUserId))
        {
            if (!Guid.TryParse(decision.ReviewerUserId.Trim(), out var parsedUserId))
            {
                throw new InvalidOperationException($"Grupo '{groupKey}': 'reviewer_user_id' invalido.");
            }

            reviewerUserId = parsedUserId.ToString();
        }

        return new NeighborhoodCurationReview
        {
            Reviewer = reviewer,
            ReviewerUserId = reviewerUserId,
            ReviewedAtUtc = NormalizeUtcInstant(decision.ReviewedAtUtc, groupKey),
            Note = note
        };
    }

    private static IReadOnlyList<NeighborhoodCurationRepair> BuildRepairs(
        NeighborhoodCurationRepairsDocument? document)
    {
        if (document is null || document.Repairs is null || document.Repairs.Count == 0)
        {
            return [];
        }

        var repairs = new List<NeighborhoodCurationRepair>(document.Repairs.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repair in document.Repairs)
        {
            var kind = repair.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (kind is not ("set_city" or "deactivate"))
            {
                throw new InvalidOperationException(
                    $"Reparo com tipo invalido ('{repair.Kind}'); use set_city ou deactivate.");
            }

            if (!Guid.TryParse(repair.NeighborhoodId, out var neighborhoodId))
            {
                throw new InvalidOperationException($"Reparo '{kind}': 'neighborhood_id' invalido.");
            }

            if (!seen.Add(neighborhoodId.ToString()))
            {
                throw new InvalidOperationException(
                    $"Reparo duplicado para o bairro '{neighborhoodId}'.");
            }

            var reason = repair.Reason?.Trim() ?? string.Empty;
            if (reason.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Reparo '{neighborhoodId}': 'reason' obrigatoria.");
            }

            string? cityId = null;
            string? cityName = null;
            string? uf = null;

            if (kind == "set_city")
            {
                if (!Guid.TryParse(repair.CityId, out var parsedCityId))
                {
                    throw new InvalidOperationException(
                        $"Reparo set_city '{neighborhoodId}': 'city_id' obrigatorio.");
                }

                cityName = repair.CityName?.Trim();
                uf = repair.Uf?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(cityName) || uf is null || uf.Length != 2)
                {
                    throw new InvalidOperationException(
                        $"Reparo set_city '{neighborhoodId}': 'city_name' e 'uf' obrigatorios.");
                }

                cityId = parsedCityId.ToString();
            }

            var reviewer = repair.Reviewer?.Trim() ?? string.Empty;
            if (reviewer.Length == 0 || reviewer.Length > NeighborhoodAliasCurationPolicy.MaxReviewerNameLength)
            {
                throw new InvalidOperationException(
                    $"Reparo '{neighborhoodId}': 'reviewer' obrigatorio.");
            }

            repairs.Add(new NeighborhoodCurationRepair
            {
                Kind = kind,
                NeighborhoodId = neighborhoodId.ToString(),
                CityId = cityId,
                CityName = cityName,
                Uf = uf,
                Reason = reason,
                Reviewer = reviewer,
                ReviewedAtUtc = NormalizeUtcInstant(repair.ReviewedAtUtc, neighborhoodId.ToString())
            });
        }

        return repairs;
    }

    private static Dictionary<string, NeighborhoodCurationApproval> IndexApprovals(
        NeighborhoodCurationApprovalsDocument? approvals,
        HashSet<string> queueGroupKeys)
    {
        var result = new Dictionary<string, NeighborhoodCurationApproval>(StringComparer.OrdinalIgnoreCase);
        if (approvals is null || approvals.Approvals is null)
        {
            return result;
        }

        foreach (var approval in approvals.Approvals)
        {
            var key = approval.GroupKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Aprovacao sem 'group_key'.");
            }

            if (!queueGroupKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    $"Aprovacao para o grupo '{key}', que nao existe na fila de curadoria.");
            }

            if (!result.TryAdd(key, approval))
            {
                throw new InvalidOperationException($"Aprovacao duplicada para o grupo '{key}'.");
            }
        }

        return result;
    }

    private static NeighborhoodCurationReview ValidateApproval(
        string groupKey,
        NeighborhoodCurationApproval approval,
        NeighborhoodCurationAssessment assessment)
    {
        if (assessment.Outcome == NeighborhoodCurationOutcome.Blocked)
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}' esta BLOCKED e nao pode ser aprovado por arquivo de aprovacoes.");
        }

        if (assessment.Outcome == NeighborhoodCurationOutcome.AutoApprove)
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}' ja e AUTO_APPROVE; remova a aprovacao redundante.");
        }

        var reviewer = approval.Reviewer?.Trim() ?? string.Empty;
        if (reviewer.Length == 0 || reviewer.Length > NeighborhoodAliasCurationPolicy.MaxReviewerNameLength)
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'reviewer' obrigatorio com ate {NeighborhoodAliasCurationPolicy.MaxReviewerNameLength} caracteres.");
        }

        var note = approval.Note?.Trim() ?? string.Empty;
        if (note.Length == 0 || note.Length > NeighborhoodAliasCurationPolicy.MaxReviewNoteLength)
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'note' obrigatoria com ate {NeighborhoodAliasCurationPolicy.MaxReviewNoteLength} caracteres.");
        }

        string? reviewerUserId = null;
        if (!string.IsNullOrWhiteSpace(approval.ReviewerUserId))
        {
            if (!Guid.TryParse(approval.ReviewerUserId.Trim(), out var parsedUserId))
            {
                throw new InvalidOperationException($"Grupo '{groupKey}': 'reviewer_user_id' invalido.");
            }

            reviewerUserId = parsedUserId.ToString();
        }

        var reviewedAt = NormalizeUtcInstant(approval.ReviewedAtUtc, groupKey);

        if (!string.Equals(
                approval.ConfirmedCanonicalName?.Trim(),
                assessment.RecommendedCanonicalName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'confirmed_canonical_name' deve ser exatamente '{assessment.RecommendedCanonicalName}'.");
        }

        if (!string.Equals(approval.ConfirmedDisplayName?.Trim(), assessment.DisplayName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'confirmed_display_name' deve ser exatamente '{assessment.DisplayName}'.");
        }

        var expectedAliases = assessment.Aliases.Select(alias => alias.Name).ToHashSet(StringComparer.Ordinal);
        var confirmedAliases = approval.ConfirmedAliasNames
            .Select(name => name?.Trim() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        if (!expectedAliases.SetEquals(confirmedAliases))
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'confirmed_alias_names' deve listar exatamente: " +
                string.Join(" | ", expectedAliases.Order(StringComparer.Ordinal)));
        }

        return new NeighborhoodCurationReview
        {
            Reviewer = reviewer,
            ReviewerUserId = reviewerUserId,
            ReviewedAtUtc = reviewedAt,
            Note = note
        };
    }

    private static string NormalizeUtcInstant(string? raw, string groupKey)
    {
        var value = raw?.Trim() ?? string.Empty;
        var endsWithUtc = value.EndsWith('Z')
                          || value.EndsWith("+00:00", StringComparison.Ordinal)
                          || value.EndsWith("-00:00", StringComparison.Ordinal);

        if (!endsWithUtc
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new InvalidOperationException(
                $"Grupo '{groupKey}': 'reviewed_at_utc' deve ser um instante UTC ISO-8601 (ex.: 2026-11-16T12:00:00Z).");
        }

        return parsed.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string? FormatDistance(double? value)
        => value is null ? null : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
