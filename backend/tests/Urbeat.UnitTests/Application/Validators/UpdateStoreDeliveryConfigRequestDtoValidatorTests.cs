using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Application.Validators;

public sealed class UpdateStoreDeliveryConfigRequestDtoValidatorTests
{
    private static UpdateStoreDeliveryConfigRequestDto BuildRequest(double? maxDeliveryRadiusKm = null) => new()
    {
        DeliveryFee = 5m,
        MinimumOrderValue = 20m,
        MaxDeliveryRadiusKm = maxDeliveryRadiusKm,
    };

    [Fact]
    public async Task ValidateAsync_ShouldAcceptPositiveRadius()
    {
        var result = await new UpdateStoreDeliveryConfigRequestDtoValidator().ValidateAsync(BuildRequest(7.5));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectZeroRadius()
    {
        var result = await new UpdateStoreDeliveryConfigRequestDtoValidator().ValidateAsync(BuildRequest(0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateStoreDeliveryConfigRequestDto.MaxDeliveryRadiusKm));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectNegativeRadius()
    {
        var result = await new UpdateStoreDeliveryConfigRequestDtoValidator().ValidateAsync(BuildRequest(-1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateStoreDeliveryConfigRequestDto.MaxDeliveryRadiusKm));
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptOmittedRadius()
    {
        var result = await new UpdateStoreDeliveryConfigRequestDtoValidator().ValidateAsync(BuildRequest());

        result.IsValid.Should().BeTrue();
    }
}
