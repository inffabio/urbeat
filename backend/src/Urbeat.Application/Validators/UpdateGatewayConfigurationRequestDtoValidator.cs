using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpdateGatewayConfigurationRequestDtoValidator : AbstractValidator<UpdateGatewayConfigurationRequestDto>
{
    private static readonly string[] AllowedEnvironments = ["Sandbox", "Production"];

    public UpdateGatewayConfigurationRequestDtoValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty()
            .MaximumLength(40);

        RuleFor(x => x.Environment)
            .NotEmpty()
            .MaximumLength(20)
            .Must(value => AllowedEnvironments.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Ambiente deve ser Sandbox ou Production.");

        RuleFor(x => x.ApiBaseUrl)
            .MaximumLength(500)
            .Must(BeAbsoluteHttpUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.ApiBaseUrl))
            .WithMessage("Informe uma URL HTTP(S) válida para a API do gateway.");

        RuleFor(x => x.WebhookUrl)
            .MaximumLength(500)
            .Must(BeAbsoluteHttpUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.WebhookUrl))
            .WithMessage("Informe uma URL HTTP(S) válida para o webhook.");

        RuleFor(x => x.ApiKeySecretReference)
            .MaximumLength(300)
            .When(x => x.ApiKeySecretReference is not null);

        RuleFor(x => x.WebhookSecretReference)
            .MaximumLength(300)
            .When(x => x.WebhookSecretReference is not null);
    }

    private static bool BeAbsoluteHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
