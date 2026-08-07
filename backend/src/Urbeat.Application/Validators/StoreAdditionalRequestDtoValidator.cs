using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class StoreAdditionalRequestDtoValidator : AbstractValidator<StoreAdditionalRequestDto>
{
    public StoreAdditionalRequestDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).PrecisionScale(10, 2, true);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}
