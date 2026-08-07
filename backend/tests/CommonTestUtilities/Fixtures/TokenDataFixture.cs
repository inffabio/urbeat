using Bogus;
using Urbeat.Application.DTOs;

namespace CommonTestUtilities.Fixtures;

public static class TokenDataFixture
{
    public static LoginRequestDto BuildValidLoginRequest()
    {
        var faker = new Faker("pt_BR");
        return new LoginRequestDto
        {
            Email = faker.Internet.Email(),
            Password = faker.Internet.Password(prefix: "Aa1!")
        };
    }

    public static RegisterUserRequestDto BuildValidRegistrationRequest()
    {
        var faker = new Faker("pt_BR");
        return new RegisterUserRequestDto
        {
            FullName = faker.Person.FullName,
            Email = faker.Internet.Email(),
            Password = "SenhaForte123",
            PhoneNumber = faker.Phone.PhoneNumber("###########")
        };
    }
}
