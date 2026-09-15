namespace Urbeat.Application.DTOs;

public sealed class ProductOptionGroupDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string ChoiceType { get; init; } = "single";
    public int MinChoices { get; init; }
    public int MaxChoices { get; init; } = 1;
    public int DisplayOrder { get; init; }

    /// <summary>
    /// Template reutilizável de origem. Enviado no create/update para associar o
    /// produto a um grupo salvo da loja; retornado para indicar o estado no editar.
    /// </summary>
    public Guid? TemplateId { get; init; }

    public IReadOnlyCollection<ProductOptionItemDto> Items { get; init; } = Array.Empty<ProductOptionItemDto>();
}

/// <summary>Grupo de opções reutilizável pertencente à loja.</summary>
public sealed class ProductOptionGroupTemplateDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string ChoiceType { get; init; } = "single";
    public int MinChoices { get; init; }
    public int MaxChoices { get; init; } = 1;
    public int DisplayOrder { get; init; }
    public IReadOnlyCollection<ProductOptionItemDto> Items { get; init; } = Array.Empty<ProductOptionItemDto>();
}

public sealed class ProductOptionItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int DisplayOrder { get; init; }
}
