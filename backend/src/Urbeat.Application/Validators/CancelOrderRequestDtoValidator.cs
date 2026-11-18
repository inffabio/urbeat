using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class CancelOrderRequestDtoValidator : AbstractValidator<CancelOrderRequestDto>
{
    public CancelOrderRequestDtoValidator()
    {
        // The service stores the trimmed value, so both bounds are validated on it: a reason with
        // trailing spaces is accepted above the minimum and rejected above the maximum only when
        // the stored value would actually exceed the column.
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Informe o motivo do cancelamento.")
            .Must(reason => Trim(reason).Length >= CancelOrderRequestDto.MinimumReasonLength)
            .WithMessage($"O motivo deve ter pelo menos {CancelOrderRequestDto.MinimumReasonLength} caracteres.")
            .Must(reason => Trim(reason).Length <= CancelOrderRequestDto.MaximumReasonLength)
            .WithMessage($"O motivo deve ter no máximo {CancelOrderRequestDto.MaximumReasonLength} caracteres.")
            .OverridePropertyName(nameof(CancelOrderRequestDto.Reason));
    }

    private static string Trim(string? reason) => (reason ?? string.Empty).Trim();
}
