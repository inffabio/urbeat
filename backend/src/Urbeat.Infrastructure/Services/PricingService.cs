using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Services;

public sealed class PricingService : IPricingService
{
    public ItemPricingResultDto PriceItem(Product product, CheckoutItemRequestDto item)
    {
        var basePrice = product.Price;
        var addTotal = 0m;
        var extras = new List<string>();

        string? variationName = null;
        int? weightGrams = null;
        string? weightLabel = null;

        switch (product.SaleMode)
        {
            case "size":
            case "fixed_weight":
                if (item.VariationId is null)
                    return ItemPricingResultDto.Invalid($"Selecione um tamanho/peso para \"{product.Name}\".");

                var variation = product.Variations.FirstOrDefault(x => x.Id == item.VariationId.Value && x.IsActive);
                if (variation is null)
                    return ItemPricingResultDto.Invalid($"Variação inválida para \"{product.Name}\".");

                variationName = variation.Name;
                basePrice = variation.Price;
                break;

            case "variable_weight":
                var cfg = product.WeightConfig;
                if (cfg is null)
                    return ItemPricingResultDto.Invalid($"Configuração de peso variável não encontrada para \"{product.Name}\".");

                var grams = item.WeightGrams ?? 0;
                if (grams < cfg.MinGrams)
                    return ItemPricingResultDto.Invalid($"Peso mínimo para \"{product.Name}\" é {cfg.MinGrams} g.");
                if (grams > cfg.MaxGrams)
                    return ItemPricingResultDto.Invalid($"Peso máximo para \"{product.Name}\" é {cfg.MaxGrams} g.");
                if (cfg.IncrementGrams > 0 && (grams - cfg.MinGrams) % cfg.IncrementGrams != 0)
                    return ItemPricingResultDto.Invalid($"Peso inválido para \"{product.Name}\": use incrementos de {cfg.IncrementGrams} g a partir de {cfg.MinGrams} g.");

                weightGrams = grams;
                weightLabel = FormatWeight(grams);
                basePrice = Math.Round(cfg.PricePerKg * grams / 1000m, 2);
                break;

            default: // "single" — mantém compatibilidade com variações legadas
                if (item.VariationId is Guid vid)
                {
                    var legacyVar = product.Variations.FirstOrDefault(x => x.Id == vid && x.IsActive);
                    if (legacyVar is null)
                        return ItemPricingResultDto.Invalid($"Variação inválida para \"{product.Name}\".");
                    variationName = legacyVar.Name;
                    basePrice += legacyVar.Price;
                }
                break;
        }

        string? choiceName = null;
        if (item.ChoiceOptionId is Guid choiceId)
        {
            var choice = product.ChoiceOptions.FirstOrDefault(x => x.Id == choiceId && x.IsActive);
            if (choice is null)
                return ItemPricingResultDto.Invalid($"Opção inválida para \"{product.Name}\".");
            choiceName = choice.Name;
            addTotal += choice.Price;
        }

        if (item.AdditionalIds is not null)
        {
            foreach (var additionalId in item.AdditionalIds)
            {
                var additional = product.Additionals.FirstOrDefault(x => x.Id == additionalId && x.IsActive);
                if (additional is null)
                    return ItemPricingResultDto.Invalid($"Adicional inválido para \"{product.Name}\".");
                extras.Add(additional.Name);
                addTotal += additional.Price;
            }
        }

        // Grupos de opções — valida min/máx e sempre soma os itens selecionados.
        foreach (var group in product.OptionGroups)
        {
            var selectedIds = item.OptionGroups?
                .FirstOrDefault(g => g.GroupId == group.Id)?
                .ItemIds ?? Array.Empty<Guid>();

            if (selectedIds.Count < group.MinChoices)
                return ItemPricingResultDto.Invalid($"Grupo \"{group.Name}\": selecione ao menos {group.MinChoices} opção(ões).");

            if (selectedIds.Count > group.MaxChoices)
                return ItemPricingResultDto.Invalid($"Grupo \"{group.Name}\": selecione no máximo {group.MaxChoices} opção(ões).");

            foreach (var selectedId in selectedIds)
            {
                var optionItem = group.Items.FirstOrDefault(x => x.Id == selectedId);
                if (optionItem is null)
                    return ItemPricingResultDto.Invalid($"Grupo \"{group.Name}\": item selecionado inválido.");
                extras.Add(optionItem.Name);
                addTotal += optionItem.Price;
            }
        }

        return new ItemPricingResultDto
        {
            IsValid = true,
            UnitPrice = basePrice + addTotal,
            ProductName = product.Name,
            VariationName = variationName,
            ChoiceOptionName = choiceName,
            WeightGrams = weightGrams,
            WeightLabel = weightLabel,
            ExtraNames = extras,
        };
    }

    private static string FormatWeight(int grams)
    {
        return grams >= 1000
            ? $"{(grams / 1000m):0.##} kg"
            : $"{grams} g";
    }
}
