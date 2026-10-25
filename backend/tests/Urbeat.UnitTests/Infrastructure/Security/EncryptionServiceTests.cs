using FluentAssertions;
using Microsoft.Extensions.Options;
using Urbeat.Infrastructure.Security;

namespace Urbeat.UnitTests.Infrastructure.Security;

public sealed class EncryptionServiceTests
{
    private static EncryptionService CreateService() =>
        new(Options.Create(new EncryptionOptions
        {
            Key = "unit-test-encryption-key-with-32-chars"
        }));

    [Fact]
    public void EncryptAndDecrypt_ShouldRoundTrip()
    {
        var service = CreateService();

        var encrypted = service.Encrypt("123456");

        encrypted.Should().NotBe("123456");
        service.Decrypt(encrypted).Should().Be("123456");
    }

    [Fact]
    public void Encrypt_ShouldProduceDifferentCipherTextForDifferentValues()
    {
        var service = CreateService();

        service.Encrypt("123456").Should().NotBe(service.Encrypt("654321"));
    }
}
