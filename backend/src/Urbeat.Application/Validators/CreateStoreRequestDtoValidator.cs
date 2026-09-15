using System.Text.RegularExpressions;
using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class CreateStoreRequestDtoValidator : AbstractValidator<CreateStoreRequestDto>
{
    private static readonly Regex StoreSlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    public CreateStoreRequestDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("O nome da loja deve ter no máximo 100 caracteres.");

        RuleFor(x => x.Slug)
            .Must(slug => !string.IsNullOrWhiteSpace(slug))
            .WithMessage("A URL da loja é obrigatória.")
            .Must(slug => string.IsNullOrWhiteSpace(slug) || slug.Length >= 3)
            .WithMessage("A URL da loja deve ter pelo menos 3 caracteres.")
            .Must(slug => string.IsNullOrWhiteSpace(slug) || IsValidStoreSlug(slug))
            .WithMessage("Use apenas letras minúsculas, números e hífens, sem hífens consecutivos ou nas extremidades.")
            .MaximumLength(120)
            .WithMessage("A URL da loja deve ter no máximo 120 caracteres.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(x => x.Document)
            .MaximumLength(18)
            .Must(document => string.IsNullOrWhiteSpace(document) || DocumentValidator.IsCpfOrCnpjValid(document))
            .WithMessage("Informe um CNPJ/CPF válido.");

        RuleFor(x => x.PixKey).MaximumLength(50);
        RuleFor(x => x.WebsiteUrl).MaximumLength(500);

        RuleFor(x => x.CuisineType)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(x => x.BannerUrl)
            .MaximumLength(500);

        RuleFor(x => x.LogoUrl)
            .MaximumLength(500);

        RuleFor(x => x.InitialMinute)
            .GreaterThanOrEqualTo(0)
            .When(x => x.InitialMinute.HasValue);

        RuleFor(x => x.FinalMinute)
            .GreaterThanOrEqualTo(0)
            .When(x => x.FinalMinute.HasValue);

        RuleFor(x => x)
            .Must(x => !x.InitialMinute.HasValue || !x.FinalMinute.HasValue || x.InitialMinute.Value <= x.FinalMinute.Value)
            .WithMessage("O tempo inicial não pode ser maior que o tempo final.");

        RuleFor(x => x.MaxDeliveryRadiusKm)
            .GreaterThan(0)
            .WithMessage("O raio máximo de entrega deve ser maior que zero.");
    }

    private static bool IsValidStoreSlug(string slug) => StoreSlugPattern.IsMatch(slug);
}
