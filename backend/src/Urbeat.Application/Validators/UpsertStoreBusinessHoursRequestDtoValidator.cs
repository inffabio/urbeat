using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpsertStoreBusinessHoursRequestDtoValidator : AbstractValidator<UpsertStoreBusinessHoursRequestDto>
{
    private const int MaxShiftsPerDay = 5;

    public UpsertStoreBusinessHoursRequestDtoValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty();

        RuleFor(x => x.Items)
            .Must(items => items.Select(i => i.DayOfWeek).Distinct().Count() == items.Count)
            .WithMessage("Each day of week can be configured only once.");

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                // Closed days must not fail validation because of shifts that are hidden
                // by the editor and are never persisted for that day.
                item.When(x => x.IsOpen, () =>
                {
                    item.RuleFor(x => x.Shifts)
                        .NotEmpty()
                        .WithMessage("Open days must have at least one shift.");

                    item.RuleFor(x => x.Shifts.Count)
                        .LessThanOrEqualTo(MaxShiftsPerDay)
                        .WithMessage($"Maximum {MaxShiftsPerDay} shifts per day.");

                    item.RuleForEach(x => x.Shifts)
                        .ChildRules(shift =>
                        {
                            shift.RuleFor(x => x.StartTime)
                                .NotNull()
                                .WithMessage("Start time is required.");

                            shift.RuleFor(x => x.EndTime)
                                .NotNull()
                                .WithMessage("End time is required.");
                        });

                    item.RuleFor(x => x.Shifts)
                        .Must(shifts => !HasEqualStartAndEndTimes(shifts))
                        .WithMessage("Start and end times cannot be equal.");
                });
            });
    }

    private static bool HasEqualStartAndEndTimes(IEnumerable<StoreBusinessHourShiftDto> shifts)
        => shifts.Any(shift =>
            shift.StartTime.HasValue
            && shift.EndTime.HasValue
            && shift.StartTime.Value == shift.EndTime.Value);
}
