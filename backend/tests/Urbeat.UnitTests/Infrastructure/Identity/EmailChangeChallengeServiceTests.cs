using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Urbeat.UnitTests.Infrastructure.Identity;

public sealed class EmailChangeChallengeServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string Email = "cliente@urbeat.local";
    private const string Stamp = "security-stamp-value";

    [Fact]
    public void ValidateChallenge_ShouldAcceptFreshChallenge()
    {
        var time = new TestTimeProvider();
        var service = CreateService(time);

        var challenge = service.CreateChallenge(UserId, Email, Stamp);
        var result = service.ValidateChallenge(challenge);

        result.Valid.Should().BeTrue();
        result.UserId.Should().Be(UserId);
        result.Email.Should().Be(Email);
        result.SecurityStamp.Should().Be(Stamp);
    }

    [Fact]
    public void ValidateChallenge_ShouldRejectExpiredChallenge()
    {
        var time = new TestTimeProvider();
        var service = CreateService(time, ttlMinutes: 30);

        var challenge = service.CreateChallenge(UserId, Email, Stamp);
        time.UtcNow = time.UtcNow.AddMinutes(31);

        service.ValidateChallenge(challenge).Valid.Should().BeFalse();
    }

    [Fact]
    public void ValidateChallenge_ShouldRejectTamperedChallenge()
    {
        var time = new TestTimeProvider();
        var service = CreateService(time);

        var challenge = service.CreateChallenge(UserId, Email, Stamp);
        var tampered = challenge[..^2] + (challenge.EndsWith("AA") ? "BB" : "AA");

        service.ValidateChallenge(tampered).Valid.Should().BeFalse();
    }

    [Fact]
    public void ValidateChallenge_ShouldRejectChallengeSignedWithAnotherKey()
    {
        var time = new TestTimeProvider();
        var issuer = CreateService(time, signingKey: "issuer-signing-key-value-1234567890");
        var verifier = CreateService(time, signingKey: "different-signing-key-value-1234567");

        var challenge = issuer.CreateChallenge(UserId, Email, Stamp);

        verifier.ValidateChallenge(challenge).Valid.Should().BeFalse();
    }

    private static EmailChangeChallengeService CreateService(TestTimeProvider time, int ttlMinutes = 30, string signingKey = "test-signing-key-value-1234567890")
    {
        return new EmailChangeChallengeService(
            Options.Create(new EmailChangeChallengeOptions { SigningKey = signingKey, TtlMinutes = ttlMinutes }),
            Options.Create(new JwtOptions { Secret = "fallback-jwt-secret-value-1234567890" }),
            time);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
