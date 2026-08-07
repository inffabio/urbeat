using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpdateStoreDeliveryConfigRequestDtoValidator : AbstractValidator<UpdateStoreDeliveryConfigRequestDto>
{
    public UpdateStoreDeliveryConfigRequestDtoValidator()
    {
        RuleFor(x => x.DeliveryFee)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.MinimumOrderValue)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.FreeShippingThreshold)
            .GreaterThanOrEqualTo(0).When(x => x.FreeShippingThreshold.HasValue);

        RuleForEach(x => x.DeliveryAreas).ChildRules(area =>
        {
            area.RuleFor(x => x.Neighborhood).NotEmpty().MaximumLength(80);
            area.RuleFor(x => x.DeliveryFee).GreaterThanOrEqualTo(0);
        });
    }
}
