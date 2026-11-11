using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Persistence;

public static class CuisineTypeDefaults
{
    public static readonly IReadOnlyList<string> Names =
    [
        "Açaíteria",
        "Batata Recheada",
        "Cachorro-quente (Hot Dog)",
        "Cafeteria",
        "Caldos e Sopas",
        "Comida Japonesa",
        "Comida Saudável (Fitness)",
        "Confeitaria",
        "Creperia",
        "Doceria",
        "Espetinhos e Churrasco",
        "Hamburgueria",
        "Marmitaria",
        "Massas e Lasanhas",
        "Padaria",
        "Pastelaria",
        "Pizzaria",
        "Salgaderia",
        "Sorveteria",
        "Tapiocaria"
    ];

    private static readonly HashSet<string> NormalizedNames =
        Names.Select(CuisineType.NormalizeName).ToHashSet(StringComparer.Ordinal);

    public static bool IsDefaultName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        return Names.Any(candidate => string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    // Compara usando a chave canônica (sem acentos/caixa), evitando que uma categoria privada
    // seja criada com um nome que difere de um padrão apenas por acentuação.
    public static bool IsDefaultNormalizedName(string? normalizedName)
    {
        return !string.IsNullOrWhiteSpace(normalizedName) && NormalizedNames.Contains(normalizedName);
    }
}
