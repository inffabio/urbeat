using System.Globalization;
using System.Text;

namespace Urbeat.Domain.Entities;

public sealed class CuisineType : BaseEntity
{
    private string _name = string.Empty;

    public CuisineType() { }

    public CuisineType(Guid id, string name) : base(id)
    {
        Name = name;
        IsActive = true;
    }

    public string Name
    {
        get => _name;
        set
        {
            _name = value ?? string.Empty;
            NormalizedName = NormalizeName(_name);
        }
    }

    // Chave canônica para índices únicos: sem acentos, minúscula e sem espaços nas bordas.
    public string NormalizedName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Categorias protegidas do sistema. Não podem ser renomeadas ou removidas por fluxos de loja.
    public bool IsDefault { get; set; }

    // Nulo identifica uma categoria global padrão; preenchido identifica uma categoria privada da loja.
    public Guid? StoreId { get; set; }

    public Store? Store { get; set; }

    // Relacionamento 1:N com as Lojas (Validar se a categoria está em uso)
    public ICollection<Store> Stores { get; set; } = new List<Store>();

    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
