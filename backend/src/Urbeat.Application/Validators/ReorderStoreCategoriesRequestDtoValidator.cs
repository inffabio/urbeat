using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class ReorderStoreCategoriesRequestDtoValidator : AbstractValidator<ReorderStoreCategoriesItemDto>
{
    public ReorderStoreCategoriesRequestDtoValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();
        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0);
    }
}
