using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Urbeat.Infrastructure.Identity;

namespace Urbeat.UnitTests.Infrastructure.Identity;

public sealed class TokenHasherTests
{
    private const string RawToken = "2c1d8c7f8a5e4a2b9c0d1e2f3a4b5c6d";

    [Fact]
    public void Hash_ShouldReturnAFastVersionedDigest()
    {
        var hasher = new TokenHasher();

        var hash = hasher.Hash(RawToken);

        hash.Should().StartWith("t1.");
        // SHA-256 in base64url is 43 characters; the digest must stay far below a password hash.
        hash.Length.Should().BeLessThan(64);
        hash.Should().NotContain(RawToken);
        hash.Should().NotContainEquivalentOf("argon2");
    }

    [Fact]
    public void Hash_ShouldBeDeterministic()
    {
        var hasher = new TokenHasher();

        hasher.Hash(RawToken).Should().Be(hasher.Hash(RawToken));
        hasher.Hash(RawToken).Should().NotBe(hasher.Hash(RawToken + "-other"));
    }

    [Fact]
    public void Verify_ShouldAcceptTheMatchingToken()
    {
        var hasher = new TokenHasher();

        var hash = hasher.Hash(RawToken);

        hasher.Verify(RawToken, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldRejectAnotherToken()
    {
        var hasher = new TokenHasher();

        var hash = hasher.Hash(RawToken);

        hasher.Verify(RawToken + "-tampered", hash).Should().BeFalse();
        hasher.Verify("", hash).Should().BeFalse();
        hasher.Verify(RawToken, "").Should().BeFalse();
        hasher.Verify(RawToken, null!).Should().BeFalse();
    }

    [Fact]
    public void Verify_ShouldRejectAPasswordHashUsedAsATokenHash()
    {
        var tokenHasher = new TokenHasher();
        var user = new IdentityUser<Guid> { Id = Guid.NewGuid() };
        var passwordHasher = new Argon2idPasswordHasher(Options.Create(new Argon2idPasswordHasherOptions
        {
            MemorySizeKb = 8 * 1024,
            Iterations = 1,
            DegreeOfParallelism = 1
        }));
        var passwordHash = passwordHasher.HashPassword(user, "SenhaForte123");

        tokenHasher.Verify("SenhaForte123", passwordHash).Should().BeFalse();
        tokenHasher.Verify(RawToken, passwordHash).Should().BeFalse();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("t1.")]
    [InlineData("t1.!!!")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=2$c2FsdA$aGFzaA")]
    public void Verify_ShouldRejectMalformedHashesWithoutThrowing(string hash)
    {
        var hasher = new TokenHasher();

        var act = () => hasher.Verify(RawToken, hash);

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    [Fact]
    public void Hash_ShouldRejectAnEmptyToken()
    {
        var hasher = new TokenHasher();

        var act = () => hasher.Hash(" ");

        act.Should().Throw<ArgumentException>();
    }
}
