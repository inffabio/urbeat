using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Application.Validators;

public sealed class UpsertStoreBusinessHoursRequestDtoValidatorTests
{
    private static StoreBusinessHourShiftDto Shift(string start, string end) => new()
    {
        StartTime = TimeOnly.Parse(start),
        EndTime = TimeOnly.Parse(end),
    };

    private static StoreBusinessHourItemDto Day(
        DayOfWeek dayOfWeek,
        bool isOpen,
        params StoreBusinessHourShiftDto[] shifts) => new()
    {
        DayOfWeek = dayOfWeek,
        IsOpen = isOpen,
        Shifts = shifts,
    };

    private static UpsertStoreBusinessHoursRequestDto BuildRequest(params StoreBusinessHourItemDto[] items) => new()
    {
        Items = items,
    };

    [Fact]
    public async Task ValidateAsync_ShouldAcceptNonOverlappingWeekSchedule()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Monday, true, Shift("09:00", "12:00"), Shift("13:00", "18:00")),
            Day(DayOfWeek.Tuesday, true, Shift("09:00", "18:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReportOnlyUniquenessForDuplicatedDayOfWeek()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Monday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Monday, true, Shift("09:00", "12:00")),
            Day(DayOfWeek.Tuesday, true, Shift("00:30", "02:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x => x.ErrorMessage == "Each day of week can be configured only once.");
        result.Errors.Should().NotContain(x => x.ErrorMessage == "Overnight shifts cannot overlap the next day's shifts.");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectEqualStartAndEndTimes()
    {
        var request = BuildRequest(Day(DayOfWeek.Monday, true, Shift("09:00", "09:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.ErrorMessage == "Start and end times cannot be equal.");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectOverlappingShiftsWithinTheSameDay()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Monday, true, Shift("00:30", "02:00"), Shift("23:00", "01:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.ErrorMessage == "Shifts cannot overlap within the same day.");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectOvernightShiftConflictWithNextDayStart()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Monday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Tuesday, true, Shift("00:30", "02:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpsertStoreBusinessHoursRequestDto.Items));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectOvernightShiftConflictFromSundayToMonday()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Sunday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Monday, true, Shift("00:30", "02:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpsertStoreBusinessHoursRequestDto.Items));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectOvernightShiftConflictFromSaturdayToSunday()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Saturday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Sunday, true, Shift("00:30", "02:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpsertStoreBusinessHoursRequestDto.Items));
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptOvernightShiftWhenNextDayStartsAfterItEnds()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Sunday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Monday, true, Shift("02:00", "03:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptOvernightShiftWhenNextDayIsClosed()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Sunday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Monday, false));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }
}
