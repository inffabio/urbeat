using Urbeat.Domain.Geo;

namespace Urbeat.Application.Geo.Curation;

/// <summary>
/// One non-canonical spelling that will be registered as an alias when the decision is applied.
/// The identifier is the legacy/collation row that keeps its own name and identifier; only the
/// lookup text is registered as an alias of the canonical neighborhood.
/// </summary>
public sealed record NeighborhoodCurationAliasClaim(Guid NeighborhoodId, string Name, string NormalizedName);

/// <summary>Result of evaluating a single queue group against the conservative curation policy.</summary>
public sealed record NeighborhoodCurationAssessment(
    NeighborhoodCurationOutcome Outcome,
    Guid? CityId,
    Guid? CanonicalNeighborhoodId,
    string CanonicalName,
    string RecommendedCanonicalName,
    string DisplayName,
    IReadOnlyList<NeighborhoodCurationAliasClaim> Aliases,
    double? DistanceMaxKm,
    double? DistanceMinKm,
    IReadOnlyList<NeighborhoodCurationReason> Reasons)
{
    public bool IsApplyable => Outcome.IsApplyable();
}

/// <summary>
/// Conservative, executable policy for auto-approving neighborhood spellings. It never merges,
/// renames or deactivates rows: it only decides whether one explicit alias may point at an official
/// CNEFE canonical neighborhood of the same city.
///
/// Every rule has a stable code so the manifest can explain, for each group, exactly why it was
/// auto-approved, sent to human review or blocked. Blocking findings can never be overridden by an
/// approvals file; only <see cref="NeighborhoodCurationOutcome.HumanReview"/> decisions can.
/// </summary>
public sealed class NeighborhoodAliasCurationPolicy
{
    /// <summary>Bump whenever a rule changes so previously generated manifests stop applying.</summary>
    public const int Version = 1;

    /// <summary>Maximum centroid distance for automatic approval.</summary>
    public const double AutoApproveMaxDistanceKm = 0.5;

    /// <summary>Above this distance the group is blocked instead of reviewed.</summary>
    public const double BlockMaxDistanceKm = 2.0;

    public const int MaxAliasLength = 150;
    public const int MaxNormalizedAliasLength = 255;
    public const int MaxDisplayNameLength = NeighborhoodName.NeighborhoodDisplayNameMaxLength;
    public const int MinNormalizedNameLength = 4;
    public const int MaxReviewerNameLength = 100;
    public const int MaxReviewNoteLength = 300;

    /// <summary>Portuguese articles/prepositions that may legitimately differ between spellings.</summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "de", "da", "do", "das", "dos", "e", "d"
    };

    /// <summary>Locality markers that always require field/official confirmation, never an alias.</summary>
    private static readonly string[] BlockedMarkerPhrases =
    [
        "aldeia",
        "indigena",
        "terra indigena",
        "reserva indigena",
        "maloca",
        "assentamento",
        "acampamento",
        "quilombo",
        "zona rural",
        "area rural",
        "gleba",
        "comunidade rural"
    ];

    private static readonly HashSet<string> GenericTokens = new(StringComparer.Ordinal)
    {
        "centro", "sede", "distrito", "municipio", "bairro", "cidade", "vilarejo",
        "zona urbana", "area urbana", "perimetro urbano", "localidade"
    };

    /// <summary>Human-readable rule sheet persisted next to the manifest for audit.</summary>
    public static IReadOnlyList<string> Rules { get; } =
    [
        "exatamente dois membros no grupo (um canonico oficial CNEFE e um legado)",
        "ambos os membros ativos na consulta de origem (revalidado no apply)",
        "um unico membro oficial_cnefe; nenhum membro manual",
        "canonical_id igual ao membro oficial e canonical_name igual ao nome do membro",
        "CityId valido, UF de duas letras e IBGE de sete digitos",
        "nenhuma StoreDeliveryArea no grupo, no canonico ou no alias (revalidado no apply)",
        "exatamente um nome em maiusculas no grupo, e ele e o canonico CNEFE",
        "distancia maxima e minima <= 0,5 km para autoaprovacao; > 2 km bloqueia",
        "diferenca aceita: apenas artigos/preposicoes (de, da, do, das, dos, e) mantendo ordem e plural",
        "diferenca de plural ou de ordem sempre exige revisao humana",
        "canonico sem acentos exige revisao humana da grafia",
        "nomes iguais apos normalizacao sao duplicidade de cadastro: bloqueado (exige consolidacao)",
        "marcadores indigenas/rurais e nomes genericos nunca sao autoaprovados",
        "canonico armazenado nunca e renomeado; display e apenas apresentacao em maiusculas com acentos"
    ];

    public NeighborhoodCurationAssessment Assess(NeighborhoodCurationQueueGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var reasons = new List<NeighborhoodCurationReason>();
        var hasBlock = false;
        var hasReview = false;

        void Add(NeighborhoodCurationReason reason)
        {
            var existing = reasons.FindIndex(reasonItem => reasonItem.Code == reason.Code);
            if (existing >= 0)
            {
                // The same finding can be produced by two guards (e.g. missing CNEFE); keep one entry
                // with the strongest severity so the review report is not duplicated.
                if (reason.Severity > reasons[existing].Severity)
                {
                    reasons[existing] = reason;
                }
            }
            else
            {
                reasons.Add(reason);
            }

            hasBlock |= reason.Severity == NeighborhoodCurationSeverity.Block;
            hasReview |= reason.Severity == NeighborhoodCurationSeverity.Review;
        }

        var canonicalName = group.CanonicalName?.Trim() ?? string.Empty;
        var canonicalNormalized = NeighborhoodName.Normalize(canonicalName);

        var recommendedRaw = string.IsNullOrWhiteSpace(group.RecommendedCanonicalName)
            ? canonicalName
            : group.RecommendedCanonicalName.Trim();
        if (recommendedRaw.Length == 0)
        {
            recommendedRaw = canonicalName;
        }

        var displayName = NeighborhoodName.ToUppercaseDisplay(recommendedRaw, MaxDisplayNameLength);

        // --- City scope -------------------------------------------------------
        Guid? cityId = Guid.TryParse(group.CityId, out var parsedCityId) ? parsedCityId : null;
        if (cityId is null)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.MissingCityId,
                "CityId ausente ou invalido: alias nunca atravessa cidade."));
        }

        if (!IsValidUf(group.Uf))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.MissingUf,
                "UF ausente ou invalida: sem UF nao ha confirmacao de cidade."));
        }

        if (!IsValidIbge(group.Ibge))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.InvalidIbge,
                "Codigo IBGE ausente ou fora do formato de sete digitos."));
        }

        // --- Group shape ------------------------------------------------------
        var members = group.Members;
        if (members.Count != 2)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.MemberCountNotTwo,
                $"Grupo com {members.Count} membros; a politica exige exatamente dois."));
        }

        if (group.StoreAreaCount > 0 || members.Any(member => member.StoreDeliveryAreas > 0))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.StoreDeliveryArea,
                "Grupo vinculado a StoreDeliveryArea: lojas dependem do identificador atual."));
        }

        if (group.CnefeSourceOk != true)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.MissingCnefeSourceFlag,
                "Fonte CNEFE nao confirmada para o grupo."));
        }

        if (members.Any(member => string.Equals(member.Kind, GeographicProvenance.NeighborhoodKind.Manual, StringComparison.Ordinal)))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.ManualMember,
                "Grupo contem bairro manual: a grafia nao vem de fonte oficial."));
        }

        var officialMembers = members
            .Where(member => string.Equals(member.Kind, GeographicProvenance.NeighborhoodKind.OfficialCnefe, StringComparison.Ordinal))
            .ToList();

        if (officialMembers.Count != 1)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.MissingOfficialCnefe,
                $"Grupo com {officialMembers.Count} membros official_cnefe; a politica exige exatamente um."));
        }

        if (!string.Equals(group.CanonicalKind, GeographicProvenance.NeighborhoodKind.OfficialCnefe, StringComparison.Ordinal))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.MissingOfficialCnefe,
                "Canonico recomendado nao e official_cnefe."));
        }

        // --- Uppercase conflict ----------------------------------------------
        var uppercaseMembers = members.Where(member => member.IsUpper).ToList();
        if (uppercaseMembers.Count != 1)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.UppercaseConflict,
                $"Grupo com {uppercaseMembers.Count} nomes em maiusculas; maiusculas nao sao prova de grafia."));
        }
        else if (officialMembers.Count == 1
                 && !string.Equals(uppercaseMembers[0].Id, officialMembers[0].Id, StringComparison.OrdinalIgnoreCase))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.UppercaseConflict,
                "O unico nome em maiusculas nao e o membro CNEFE canonico."));
        }

        // --- Canonical identity ----------------------------------------------
        Guid? canonicalId = Guid.TryParse(group.CanonicalId, out var parsedCanonicalId) ? parsedCanonicalId : null;
        if (canonicalId is null)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.InvalidIdentifier,
                "CanonicalId ausente ou invalido."));
        }

        NeighborhoodCurationQueueMember? canonicalMember = null;
        Guid? canonicalMemberId = null;
        if (officialMembers.Count == 1)
        {
            canonicalMember = officialMembers[0];
            if (!Guid.TryParse(canonicalMember.Id, out var officialId))
            {
                Add(NeighborhoodCurationReason.Block(
                    NeighborhoodCurationReasonCodes.InvalidIdentifier,
                    "Identificador do membro oficial invalido."));
            }
            else
            {
                canonicalMemberId = officialId;
                if (canonicalId is not null && officialId != canonicalId)
                {
                    Add(NeighborhoodCurationReason.Block(
                        NeighborhoodCurationReasonCodes.CanonicalMismatch,
                        "CanonicalId nao aponta para o membro official_cnefe."));
                }
            }

            if (!string.Equals(canonicalMember.Name?.Trim(), canonicalName, StringComparison.Ordinal))
            {
                Add(NeighborhoodCurationReason.Block(
                    NeighborhoodCurationReasonCodes.CanonicalMismatch,
                    "canonical_name diverge do nome do membro oficial."));
            }
        }
        else
        {
            // Without an official member the canonical is still identified by canonical_id, so the
            // canonical row is never mistaken for one of its own aliases.
            canonicalMemberId = canonicalId;
        }

        if (canonicalName.Length == 0)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.MissingCanonicalName,
                "Nome canonico ausente."));
        }
        else if (canonicalNormalized.Length < MinNormalizedNameLength)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.InvalidNameLength,
                "Nome canonico normalizado curto demais para decisao automatica."));
        }

        if (recommendedRaw.Length > 0
            && !string.Equals(NeighborhoodName.Normalize(recommendedRaw), canonicalNormalized, StringComparison.Ordinal))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.CanonicalRecommendationMismatch,
                "Grafia recomendada muda letras, ordem ou plural do canonico atual."));
        }

        if (canonicalNormalized.Length > 0 && ContainsBlockedMarker(canonicalNormalized))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.IndigenousOrRuralMarker,
                "Nome canonico contem marcador indigena/rural: exige confirmacao oficial."));
        }

        if (canonicalNormalized.Length > 0 && IsGenericName(canonicalNormalized))
        {
            Add(NeighborhoodCurationReason.Review(
                NeighborhoodCurationReasonCodes.GenericName,
                "Nome canonico generico (token unico ou expressao administrativa)."));
        }

        // --- Distance ---------------------------------------------------------
        var distanceMax = NormalizeDistance(group.Distance);
        var distanceMin = NormalizeDistance(group.DistanceMinKm);
        if (distanceMax is null)
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.MissingDistance,
                "Distancia ausente: nao ha confirmacao geografica."));
        }
        else if (distanceMax > BlockMaxDistanceKm || (distanceMin is not null && distanceMin > BlockMaxDistanceKm))
        {
            Add(NeighborhoodCurationReason.Block(
                NeighborhoodCurationReasonCodes.DistanceAboveBlockLimit,
                $"Distancia maxima {distanceMax:0.###} km acima de {BlockMaxDistanceKm:0.#} km."));
        }
        else if (distanceMax > AutoApproveMaxDistanceKm)
        {
            Add(NeighborhoodCurationReason.Review(
                NeighborhoodCurationReasonCodes.DistanceAboveAutoLimit,
                $"Distancia maxima {distanceMax:0.###} km acima do limite automatico de {AutoApproveMaxDistanceKm:0.#} km."));
        }

        // --- Variant spellings ------------------------------------------------
        var aliases = new List<NeighborhoodCurationAliasClaim>();
        foreach (var member in members)
        {
            if (canonicalMemberId is not null
                && Guid.TryParse(member.Id, out var memberId)
                && memberId == canonicalMemberId.Value)
            {
                continue;
            }

            var aliasName = member.Name?.Trim() ?? string.Empty;
            if (aliasName.Length == 0 || aliasName.Length > MaxAliasLength)
            {
                Add(NeighborhoodCurationReason.Block(
                    aliasName.Length == 0
                        ? NeighborhoodCurationReasonCodes.InvalidNameLength
                        : NeighborhoodCurationReasonCodes.AliasTooLong,
                    "Nome do alias vazio ou acima de 150 caracteres."));
                continue;
            }

            var normalized = NeighborhoodName.Normalize(aliasName);
            if (normalized.Length < MinNormalizedNameLength)
            {
                Add(NeighborhoodCurationReason.Block(
                    NeighborhoodCurationReasonCodes.InvalidNameLength,
                    "Nome do alias normalizado curto demais para decisao automatica."));
                continue;
            }

            if (normalized.Length > MaxNormalizedAliasLength)
            {
                Add(NeighborhoodCurationReason.Block(
                    NeighborhoodCurationReasonCodes.AliasTooLong,
                    "Nome do alias normalizado acima de 255 caracteres."));
                continue;
            }

            if (!Guid.TryParse(member.Id, out var aliasId))
            {
                Add(NeighborhoodCurationReason.Block(
                    NeighborhoodCurationReasonCodes.InvalidIdentifier,
                    "Identificador do alias invalido."));
                continue;
            }

            if (string.Equals(normalized, canonicalNormalized, StringComparison.Ordinal))
            {
                Add(NeighborhoodCurationReason.Block(
                    NeighborhoodCurationReasonCodes.DuplicateNormalizedName,
                    "Alias e canonico tem o mesmo nome normalizado: e duplicidade de cadastro e exige consolidacao, nao alias."));
                continue;
            }

            if (ContainsBlockedMarker(normalized))
            {
                Add(NeighborhoodCurationReason.Block(
                    NeighborhoodCurationReasonCodes.IndigenousOrRuralMarker,
                    "Nome do alias contem marcador indigena/rural: exige confirmacao oficial."));
                continue;
            }

            if (IsGenericName(normalized))
            {
                Add(NeighborhoodCurationReason.Review(
                    NeighborhoodCurationReasonCodes.GenericName,
                    "Nome do alias generico (token unico ou expressao administrativa)."));
            }

            aliases.Add(new NeighborhoodCurationAliasClaim(aliasId, aliasName, normalized));
        }

        // Only classify the textual variation when the group really has one canonical + one alias.
        if (canonicalMemberId is not null
            && members.Count == 2
            && aliases.Count == 1
            && canonicalNormalized.Length > 0)
        {
            ClassifyVariation(canonicalNormalized, aliases[0].NormalizedName, Add);
        }

        if (group.AccentInferiority == true)
        {
            Add(NeighborhoodCurationReason.Review(
                NeighborhoodCurationReasonCodes.CanonicalMissingAccents,
                "Canonico CNEFE perde acentos presentes no alias: grafia correta exige revisao humana."));
        }

        var outcome = hasBlock
            ? NeighborhoodCurationOutcome.Blocked
            : hasReview
                ? NeighborhoodCurationOutcome.HumanReview
                : NeighborhoodCurationOutcome.AutoApprove;

        if (outcome == NeighborhoodCurationOutcome.AutoApprove)
        {
            reasons.Add(NeighborhoodCurationReason.Info(
                NeighborhoodCurationReasonCodes.SameCityConfirmed,
                "Mesma cidade confirmada por CityId/UF/IBGE."));
            reasons.Add(NeighborhoodCurationReason.Info(
                NeighborhoodCurationReasonCodes.OfficialCnefeCanonical,
                "Canonico e o unico membro official_cnefe do grupo."));
            reasons.Add(NeighborhoodCurationReason.Info(
                NeighborhoodCurationReasonCodes.SingleUppercaseCanonical,
                "Existe exatamente um nome em maiusculas e ele e o canonico."));
            reasons.Add(NeighborhoodCurationReason.Info(
                NeighborhoodCurationReasonCodes.DistanceWithinAutoLimit,
                $"Distancias dentro de {AutoApproveMaxDistanceKm:0.#} km."));
        }

        return new NeighborhoodCurationAssessment(
            outcome,
            cityId,
            canonicalId,
            canonicalName,
            recommendedRaw,
            displayName,
            aliases,
            distanceMax,
            distanceMin,
            reasons);
    }

    /// <summary>
    /// Classifies how the alias text differs from the canonical text. Only stopword removal
    /// (articles/prepositions) is considered equivalent; anything else raises a review finding.
    /// Plural/order heuristics are intentionally conservative: they can only escalate to review,
    /// never auto-approve.
    /// </summary>
    private static void ClassifyVariation(string canonicalNormalized, string aliasNormalized, Action<NeighborhoodCurationReason> add)
    {
        if (string.Equals(canonicalNormalized, aliasNormalized, StringComparison.Ordinal))
        {
            return;
        }

        var canonicalTokens = canonicalNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var aliasTokens = aliasNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var canonicalCore = canonicalTokens.Where(token => !Stopwords.Contains(token)).ToArray();
        var aliasCore = aliasTokens.Where(token => !Stopwords.Contains(token)).ToArray();

        if (canonicalCore.Length > 0
            && canonicalCore.SequenceEqual(aliasCore, StringComparer.Ordinal))
        {
            add(NeighborhoodCurationReason.Info(
                NeighborhoodCurationReasonCodes.StopwordOnlyVariation,
                "Diferenca apenas de artigos/preposicoes, mantendo ordem e plural."));
            return;
        }

        // A pure permutation is an order divergence, never a letter divergence.
        if (canonicalTokens.Length == aliasTokens.Length
            && canonicalTokens.Order(StringComparer.Ordinal).SequenceEqual(aliasTokens.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            add(NeighborhoodCurationReason.Review(
                NeighborhoodCurationReasonCodes.TokenOrderDivergence,
                "Alias difere por ordem dos tokens: exige revisao."));
            return;
        }

        if (canonicalTokens.Length == aliasTokens.Length)
        {
            var plural = false;
            var otherDifference = false;
            for (var index = 0; index < canonicalTokens.Length; index++)
            {
                if (string.Equals(canonicalTokens[index], aliasTokens[index], StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsPluralPair(canonicalTokens[index], aliasTokens[index]))
                {
                    plural = true;
                }
                else
                {
                    otherDifference = true;
                }
            }

            if (plural)
            {
                add(NeighborhoodCurationReason.Review(
                    NeighborhoodCurationReasonCodes.PluralDivergence,
                    "Alias difere por plural: exige revisao da forma correta."));
            }

            if (otherDifference)
            {
                add(NeighborhoodCurationReason.Review(
                    NeighborhoodCurationReasonCodes.TokenDivergence,
                    "Alias difere em letras que nao sao acento, pontuacao ou artigo/preposicao."));
            }

            if (plural || otherDifference)
            {
                return;
            }
        }

        if (canonicalCore.Length > 0
            && canonicalCore.Length == aliasCore.Length
            && canonicalCore.Order(StringComparer.Ordinal).SequenceEqual(aliasCore.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            add(NeighborhoodCurationReason.Review(
                NeighborhoodCurationReasonCodes.TokenOrderDivergence,
                "Alias difere por ordem dos tokens (desconsiderando artigos/preposicoes): exige revisao."));
            return;
        }

        add(NeighborhoodCurationReason.Review(
            NeighborhoodCurationReasonCodes.TokenDivergence,
            "Alias difere em letras/silabas que nao sao acento, pontuacao ou artigo/preposicao."));
    }

    private static bool IsPluralPair(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(Singularize(left), Singularize(right), StringComparison.Ordinal);
    }

    private static string Singularize(string token)
    {
        if (token.Length > 3 && token.EndsWith("oes", StringComparison.Ordinal))
        {
            return token[..^3] + "ao";
        }

        if (token.Length > 3 && token.EndsWith("aes", StringComparison.Ordinal))
        {
            return token[..^3] + "ao";
        }

        if (token.Length > 3 && token.EndsWith("ais", StringComparison.Ordinal))
        {
            return token[..^3] + "al";
        }

        if (token.Length > 3 && token.EndsWith("eis", StringComparison.Ordinal))
        {
            return token[..^3] + "el";
        }

        if (token.Length > 3 && token.EndsWith("ois", StringComparison.Ordinal))
        {
            return token[..^3] + "ol";
        }

        if (token.Length > 3 && token.EndsWith("ns", StringComparison.Ordinal))
        {
            return token[..^2] + "m";
        }

        if (token.Length > 3 && token.EndsWith("es", StringComparison.Ordinal))
        {
            return token[..^2];
        }

        if (token.Length > 3 && token.EndsWith('s'))
        {
            return token[..^1];
        }

        return token;
    }

    private static bool ContainsBlockedMarker(string normalizedName)
    {
        var tokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var phrase in BlockedMarkerPhrases)
        {
            var phraseTokens = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (ContainsSequence(tokens, phraseTokens))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSequence(string[] tokens, string[] sequence)
    {
        if (sequence.Length == 0 || sequence.Length > tokens.Length)
        {
            return false;
        }

        for (var start = 0; start <= tokens.Length - sequence.Length; start++)
        {
            var match = true;
            for (var offset = 0; offset < sequence.Length; offset++)
            {
                if (!string.Equals(tokens[start + offset], sequence[offset], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGenericName(string normalizedName)
    {
        var core = normalizedName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !Stopwords.Contains(token))
            .ToArray();

        if (core.Length == 0)
        {
            return true;
        }

        if (core.Length == 1)
        {
            // A single token is inherently ambiguous inside a city ("CENTRO", "SEDE", "DISTRITO").
            return true;
        }

        if (core.All(GenericTokens.Contains))
        {
            return true;
        }

        return GenericTokens.Contains(string.Join(' ', core));
    }

    private static bool IsValidUf(string? uf)
    {
        if (string.IsNullOrWhiteSpace(uf) || uf.Trim().Length != 2)
        {
            return false;
        }

        return uf.Trim().All(character => char.IsAsciiLetter(character));
    }

    private static bool IsValidIbge(string? ibge)
    {
        if (string.IsNullOrWhiteSpace(ibge) || ibge.Trim().Length != 7)
        {
            return false;
        }

        return ibge.Trim().All(char.IsAsciiDigit);
    }

    private static double? NormalizeDistance(double? value)
    {
        if (value is null || !double.IsFinite(value.Value) || value.Value < 0)
        {
            return null;
        }

        return Math.Round(value.Value, 3, MidpointRounding.AwayFromZero);
    }
}
