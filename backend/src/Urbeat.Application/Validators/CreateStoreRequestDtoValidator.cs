using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class CreateStoreRequestDtoValidator : AbstractValidator<CreateStoreRequestDto>
{
    public CreateStoreRequestDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.Slug)
            .MaximumLength(120);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(x => x.Document)
            .MaximumLength(18)
            .Must(document => string.IsNullOrWhiteSpace(document) || DocumentValidator.IsCpfOrCnpjValid(document))
            .WithMessage("Informe um CNPJ/CPF válido.");

        RuleFor(x => x.PixKey).MaximumLength(50);
        RuleFor(x => x.InstagramUrl).MaximumLength(500);
        RuleFor(x => x.FacebookUrl).MaximumLength(500);
        RuleFor(x => x.TikTokUrl).MaximumLength(500);
        RuleFor(x => x.WebsiteUrl).MaximumLength(500);

        RuleFor(x => x.Description)
            .MaximumLength(500);

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
}
