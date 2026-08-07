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
                item.RuleFor(x => x.Shifts)
                    .NotEmpty()
                    .When(x => x.IsOpen)
                    .WithMessage("Open days must have at least one shift.");

                item.RuleFor(x => x.Shifts.Count)
                    .LessThanOrEqualTo(MaxShiftsPerDay)
                    .WithMessage($"Maximum {MaxShiftsPerDay} shifts per day.");

                item.RuleForEach(x => x.Shifts)
                    .ChildRules(shift =>
                    {
                        shift.RuleFor(x => x.StartTime)
                            .NotEmpty()
                            .WithMessage("Start time is required.");

                        shift.RuleFor(x => x.EndTime)
                            .NotEmpty()
                            .WithMessage("End time is required.");
                    });

                item.RuleFor(x => x.Shifts)
                    .Must(shifts =>
                    {
                        var list = shifts.ToList();
                        for (int i = 0; i < list.Count; i++)
                        {
                            for (int j = i + 1; j < list.Count; j++)
                            {
                                if (ShiftsOverlap(list[i], list[j]))
                                    return false;
                            }
                        }
                        return true;
                    })
                    .WithMessage("Shifts cannot overlap within the same day.");

                item.RuleFor(x => x.Shifts)
                    .Must(shifts => shifts.All(s => s.StartTime != s.EndTime))
                    .WithMessage("Start and end times cannot be equal.");
            });
    }

    private static bool ShiftsOverlap(StoreBusinessHourShiftDto a, StoreBusinessHourShiftDto b)
    {
        var aStart = a.StartTime.ToTimeSpan().TotalMinutes;
        var aEnd = a.EndTime.ToTimeSpan().TotalMinutes;
        if (aEnd <= aStart) aEnd += 1440;

        var bStart = b.StartTime.ToTimeSpan().TotalMinutes;
        var bEnd = b.EndTime.ToTimeSpan().TotalMinutes;
        if (bEnd <= bStart) bEnd += 1440;

        return aStart < bEnd && bStart < aEnd
            || (aStart < bEnd + 1440 && bStart + 1440 < aEnd)
            || (aStart + 1440 < bEnd && bStart < aEnd + 1440);
    }
}
