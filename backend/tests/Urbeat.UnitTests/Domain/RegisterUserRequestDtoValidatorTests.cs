using CommonTestUtilities.Fixtures;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class RegisterUserRequestDtoValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldReturnValidResult_WhenRequestIsValid()
    {
        var validator = new RegisterUserRequestDtoValidator();
        var request = TokenDataFixture.BuildValidRegistrationRequest();

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenPasswordIsTooShort()
    {
        var validator = new RegisterUserRequestDtoValidator();
        var request = TokenDataFixture.BuildValidRegistrationRequest();
        request = new RegisterUserRequestDto
        {
            FullName = request.FullName,
            Email = request.Email,
            Password = "123",
            PhoneNumber = request.PhoneNumber
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }
}
