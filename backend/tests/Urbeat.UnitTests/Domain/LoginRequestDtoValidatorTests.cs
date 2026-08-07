using CommonTestUtilities.Fixtures;
using FluentAssertions;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class LoginRequestDtoValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldReturnValidResult_WhenRequestIsValid()
    {
        var validator = new LoginRequestDtoValidator();
        var request = TokenDataFixture.BuildValidLoginRequest();

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenEmailIsInvalid()
    {
        var validator = new LoginRequestDtoValidator();
        var request = TokenDataFixture.BuildValidLoginRequest();
        request = new()
        {
            Email = "email-invalido",
            Password = request.Password
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }
}
