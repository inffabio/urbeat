using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class BatchUpsertProductsRequestDtoValidator : AbstractValidator<BatchUpsertProductsRequestDto>
{
    public BatchUpsertProductsRequestDtoValidator()
    {
        // O batch substitui todo o catálogo: sem a coleção não há como distinguir
        // "nenhum item" de "payload incompleto", então null/ausente é inválido.
        RuleFor(x => x.Items)
            .NotNull()
            .WithMessage("A lista de itens é obrigatória. Envie um array, mesmo que vazio, para limpar o catálogo.");

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.SpecialClassification)
                    .Must(classification => classification is null || Enum.IsDefined(classification.Value))
                    .WithMessage("Classificação especial inválida. Use: Offers, BestSeller ou New.");
            });
    }
}
