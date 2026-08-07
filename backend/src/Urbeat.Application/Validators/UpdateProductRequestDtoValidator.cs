using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpdateProductRequestDtoValidator : AbstractValidator<UpdateProductRequestDto>
{
    public UpdateProductRequestDtoValidator()
    {
        RuleFor(x => x.CategoryId)
            .NotEmpty()
            .WithMessage("A categoria é obrigatória.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("O nome do produto é obrigatório.")
            .MaximumLength(120)
            .WithMessage("O nome deve ter no máximo 120 caracteres.");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .WithMessage("A descrição deve ter no máximo 500 caracteres.");

        RuleFor(x => x.Price)
            .GreaterThan(0)
            .When(x => x.SaleMode == "single")
            .WithMessage("O preço deve ser maior que zero.")
            .PrecisionScale(10, 2, true);

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0)
            .When(x => x.SaleMode != "single")
            .PrecisionScale(10, 2, true);

        RuleFor(x => x.SaleMode)
            .Must(m => m is "single" or "size" or "fixed_weight" or "variable_weight")
            .WithMessage("Forma de venda inválida. Use: single, size, fixed_weight ou variable_weight.");

        RuleFor(x => x.Variations)
            .Must((req, v) => req.SaleMode is not ("size" or "fixed_weight") || (v?.Any(x => x.IsActive && x.Price > 0) is true))
            .WithMessage("Ao menos uma variação ativa com preço é obrigatória para esta forma de venda.");

        RuleFor(x => x.WeightConfig!)
            .ChildRules(cfg =>
            {
                cfg.RuleFor(w => w.PricePerKg).GreaterThan(0).PrecisionScale(10, 2, true);
                cfg.RuleFor(w => w.MinGrams).GreaterThan(0);
                cfg.RuleFor(w => w.MaxGrams).GreaterThan(w => w.MinGrams);
                cfg.RuleFor(w => w.IncrementGrams).GreaterThan(0);
            })
            .When(x => x.WeightConfig is not null);

        RuleFor(x => x.ImageUrl)
            .NotEmpty()
            .WithMessage("A imagem do produto é obrigatória.");

        RuleFor(x => x.PromotionalPrice)
            .GreaterThan(0)
            .WithMessage("O preço promocional deve ser maior que zero.")
            .PrecisionScale(10, 2, true)
            .When(x => x.PromotionalPrice.HasValue);

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0);

        RuleForEach(x => x.Additionals)
            .SetValidator(new ProductAdditionalDtoValidator());

        RuleFor(x => x.Additionals)
            .Must(a => a.Select(x => x.Name.Trim().ToLowerInvariant()).Distinct().Count() == a.Count)
            .WithMessage("Há adicionais duplicados com o mesmo nome.");

        RuleForEach(x => x.ChoiceOptions)
            .SetValidator(new ProductChoiceOptionDtoValidator());

        RuleFor(x => x.ChoiceOptions)
            .Must(c => c.Select(x => x.Name.Trim().ToLowerInvariant()).Distinct().Count() == c.Count)
            .WithMessage("Há opções de escolha duplicadas com o mesmo nome.");

        RuleForEach(x => x.Variations)
            .SetValidator(new ProductVariationDtoValidator());

        RuleFor(x => x.Variations)
            .Must(v =>
            {
                var names = v.Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .Select(x => x.Name.Trim().ToLowerInvariant()).ToList();
                return names.Distinct().Count() == names.Count;
            })
            .WithMessage("Há variações duplicadas com o mesmo nome.");

        RuleForEach(x => x.OptionGroups)
            .SetValidator(new ProductOptionGroupDtoValidator());
    }
}
