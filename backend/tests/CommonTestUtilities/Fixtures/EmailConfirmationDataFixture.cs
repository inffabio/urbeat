using Bogus;
using Urbeat.Application.DTOs;

namespace CommonTestUtilities.Fixtures;

public static class EmailConfirmationDataFixture
{
    public static ConfirmEmailRequestDto BuildValidConfirmRequest(Guid? userId = null, string? token = null)
    {
        return new ConfirmEmailRequestDto
        {
            UserId = userId ?? Guid.NewGuid(),
            Token = token ?? "Vm9jZS1lc3RhLWNvbmZpcm1hbmRvLW8tc2V1LWVtYWls"
        };
    }

    public static ResendEmailConfirmationRequestDto BuildValidResendRequest(string? email = null)
    {
        var faker = new Faker("pt_BR");
        return new ResendEmailConfirmationRequestDto
        {
            Email = email ?? faker.Internet.Email().ToLowerInvariant()
        };
    }
}
