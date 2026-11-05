using System.Text.Json;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Plans;

/// <summary>
/// Validação e (de)serialização dos benefícios de plano armazenados como JSON
/// em <see cref="Plan.BenefitsJson"/>. Mantém o contrato simples e portável
/// sem transformar benefícios informativos em autorização de recursos.
/// </summary>
public static class PlanBenefits
{
    public const int MaxItems = 20;

    public const int MaxItemLength = 200;

    public static bool TryNormalize(
        IEnumerable<string>? benefits,
        out IReadOnlyList<string> normalized,
        out string? error)
    {
        var result = new List<string>();

        if (benefits is not null)
        {
            foreach (var raw in benefits)
            {
                if (raw is null)
                {
                    normalized = Array.Empty<string>();
                    error = "Benefícios não podem conter itens nulos.";
                    return false;
                }

                var value = raw.Trim();
                if (value.Length == 0)
                {
                    normalized = Array.Empty<string>();
                    error = "Benefícios não podem conter itens vazios.";
                    return false;
                }

                if (value.Length > MaxItemLength)
                {
                    normalized = Array.Empty<string>();
                    error = $"Cada benefício deve ter no máximo {MaxItemLength} caracteres.";
                    return false;
                }

                if (!result.Contains(value, StringComparer.Ordinal))
                {
                    result.Add(value);
                }
            }

            if (result.Count > MaxItems)
            {
                normalized = Array.Empty<string>();
                error = $"O plano pode ter no máximo {MaxItems} benefícios.";
                return false;
            }
        }

        normalized = result;
        error = null;
        return true;
    }

    public static string Serialize(IEnumerable<string>? benefits)
    {
        if (!TryNormalize(benefits, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(benefits));
        }

        return JsonSerializer.Serialize(normalized);
    }

    public static IReadOnlyList<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json);
            if (values is null)
            {
                return Array.Empty<string>();
            }

            // Dados antigos/corrompidos não devem quebrar a listagem pública.
            return TryNormalize(values, out var normalized, out _)
                ? normalized
                : Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
