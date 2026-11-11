using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class ConfirmRolePromotionRequestDtoValidator : AbstractValidator<ConfirmRolePromotionRequestDto>
{
    public ConfirmRolePromotionRequestDtoValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty()
            .MaximumLength(200);
    }
}
