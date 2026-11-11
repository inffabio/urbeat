using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Urbeat.Infrastructure.Identity;

namespace Urbeat.UnitTests.Infrastructure.Identity;

public sealed class Argon2idPasswordHasherTests
{
    private const string Password = "SenhaForte123";

    private static readonly Argon2idPasswordHasherOptions FastOptions = new()
    {
        MemorySizeKb = 8 * 1024,
        Iterations = 1,
        DegreeOfParallelism = 1,
        SaltSizeBytes = 16,
        HashSizeBytes = 32
    };

    [Fact]
    public void HashPassword_ShouldUseAFreshRandomSaltForEveryHash()
    {
        var hasher = CreateHasher();
        var user = NewUser();

        var first = hasher.HashPassword(user, Password);
        var second = hasher.HashPassword(user, Password);

        first.Should().NotBe(second);
        SaltOf(first).Should().NotBe(SaltOf(second));
    }

    [Fact]
    public void HashPassword_ShouldEmitTheVersionedArgon2idFormat()
    {
        var hasher = CreateHasher();

        var hash = hasher.HashPassword(NewUser(), Password);

        hash.Should().StartWith("$urbeat-argon2id$v=1$m=8192,t=1,p=1$");
        hash.Split('$').Should().HaveCount(6);
        hash.Should().NotContain(Password);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldSucceedForTheCorrectPassword()
    {
        var hasher = CreateHasher();
        var user = NewUser();

        var hash = hasher.HashPassword(user, Password);

        hasher.VerifyHashedPassword(user, hash, Password).Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldFailForAWrongPassword()
    {
        var hasher = CreateHasher();
        var user = NewUser();

        var hash = hasher.HashPassword(user, Password);

        hasher.VerifyHashedPassword(user, hash, "SenhaErrada123").Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldSupportUnicodePasswords()
    {
        const string unicodePassword = "S3nhá-çãõ-日本-🔐";
        var hasher = CreateHasher();
        var user = NewUser();

        var hash = hasher.HashPassword(user, unicodePassword);

        hasher.VerifyHashedPassword(user, hash, unicodePassword).Should().Be(PasswordVerificationResult.Success);
        hasher.VerifyHashedPassword(user, hash, "S3nha-ção-日本-🔐").Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldRequestRehashWhenStoredParametersAreBelowTheCurrentOnes()
    {
        var weakHasher = CreateHasher(new Argon2idPasswordHasherOptions
        {
            MemorySizeKb = 8 * 1024,
            Iterations = 1,
            DegreeOfParallelism = 1,
            SaltSizeBytes = 16,
            HashSizeBytes = 32
        });
        var currentHasher = CreateHasher(new Argon2idPasswordHasherOptions
        {
            MemorySizeKb = 16 * 1024,
            Iterations = 2,
            DegreeOfParallelism = 1,
            SaltSizeBytes = 16,
            HashSizeBytes = 32
        });

        var user = NewUser();
        var weakHash = weakHasher.HashPassword(user, Password);

        currentHasher.VerifyHashedPassword(user, weakHash, Password)
            .Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldSucceedWhenStoredParametersExceedTheCurrentOnes()
    {
        var strongHasher = CreateHasher(new Argon2idPasswordHasherOptions
        {
            MemorySizeKb = 16 * 1024,
            Iterations = 2,
            DegreeOfParallelism = 2,
            SaltSizeBytes = 16,
            HashSizeBytes = 32
        });
        var currentHasher = CreateHasher(FastOptions);

        var user = NewUser();
        var strongHash = strongHasher.HashPassword(user, Password);

        currentHasher.VerifyHashedPassword(user, strongHash, Password)
            .Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldAcceptLegacyAspNetIdentityPbkdf2AndRequestRehash()
    {
        var user = NewUser();
        var legacyHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, Password);
        var hasher = CreateHasher();

        hasher.VerifyHashedPassword(user, legacyHash, Password)
            .Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
        hasher.VerifyHashedPassword(user, legacyHash, "SenhaErrada123")
            .Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldRequestRehashOnlyOnceForLegacyHashes()
    {
        var user = NewUser();
        var legacyHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, Password);
        var hasher = CreateHasher();

        var upgraded = hasher.HashPassword(user, Password);
        upgraded.Should().StartWith("$urbeat-argon2id$");
        hasher.VerifyHashedPassword(user, upgraded, Password).Should().Be(PasswordVerificationResult.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("$urbeat-argon2id$")]
    [InlineData("$urbeat-argon2id$v=1")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=1,p=1$AAAA")]
    [InlineData("$urbeat-argon2id$v=2$m=8192,t=1,p=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=1,p=1,x=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=notanumber,t=1,p=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=-1,t=1,p=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=0,t=1,p=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=0,p=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=1,p=0$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=1,p=1$!!!$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=1,p=1$AAAA$!!!")]
    [InlineData("$urbeat-argon2id$v=1$m=1048577,t=1,p=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=1000,p=1$AAAA$BBBB")]
    [InlineData("$urbeat-argon2id$v=1$m=8192,t=1,p=64$AAAA$BBBB")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=2$c2FsdA$aGFzaA")]
    public void VerifyHashedPassword_ShouldFailSafelyForMalformedOrOutOfBoundsHashes(string malformed)
    {
        var hasher = CreateHasher();

        var act = () => hasher.VerifyHashedPassword(NewUser(), malformed, Password);

        act.Should().NotThrow();
        act().Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldFailForTruncatedHashes()
    {
        var hasher = CreateHasher();
        var valid = hasher.HashPassword(NewUser(), Password);

        foreach (var length in new[] { 8, 16, valid.Length / 2, valid.Length - 2 })
        {
            var truncated = valid[..length];
            hasher.VerifyHashedPassword(NewUser(), truncated, Password)
                .Should().Be(PasswordVerificationResult.Failed);
        }
    }

    [Fact]
    public void VerifyHashedPassword_ShouldFailForAnOversizedHash()
    {
        var hasher = CreateHasher();
        var oversized = "$urbeat-argon2id$v=1$m=8192,t=1,p=1$" + new string('A', 100_000);

        hasher.VerifyHashedPassword(NewUser(), oversized, Password)
            .Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyHashedPassword_ShouldFailForNullInputsWithoutThrowing()
    {
        var hasher = CreateHasher();

        hasher.VerifyHashedPassword(NewUser(), null!, Password).Should().Be(PasswordVerificationResult.Failed);
        hasher.VerifyHashedPassword(NewUser(), hasher.HashPassword(NewUser(), Password), null!)
            .Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void HashPassword_ShouldRejectNullPassword()
    {
        var hasher = CreateHasher();

        var act = () => hasher.HashPassword(NewUser(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static Argon2idPasswordHasher CreateHasher(Argon2idPasswordHasherOptions? options = null)
        => new(Options.Create(options ?? FastOptions));

    private static IdentityUser<Guid> NewUser() => new() { Id = Guid.NewGuid(), UserName = "teste@urbeat.local" };

    private static string SaltOf(string hash) => hash.Split('$')[4];
}
