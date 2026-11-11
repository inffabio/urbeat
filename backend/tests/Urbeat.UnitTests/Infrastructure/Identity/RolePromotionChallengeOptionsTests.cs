using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Identity;

/// <summary>
/// The promotion signing key must fail closed: no JWT fallback, no committed default and no
/// placeholder. These tests also guarantee the failure messages never echo the configured value.
/// </summary>
public sealed class RolePromotionChallengeOptionsTests
{
    private const string ValidKey = "Zt7Qm2Wc9Lp4Xr8Nb3Vk6Hs1Jd5Fg0Ay";

    [Fact]
    public void Validate_ShouldAcceptARandomKey()
    {
        var options = new RolePromotionChallengeOptions { SigningKey = ValidKey };

        options.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldRejectAnEmptyKey()
    {
        var options = new RolePromotionChallengeOptions { SigningKey = "   " };

        options.Validate().Should().ContainSingle()
            .Which.Should().Contain("RolePromotionChallenge:SigningKey is required");
    }

    [Fact]
    public void Validate_ShouldRejectAKnownPlaceholderWithoutEchoingIt()
    {
        const string placeholder = "CHANGE_ME_MINIMUM_32_CHARS_SECRET";
        var options = new RolePromotionChallengeOptions { SigningKey = placeholder };

        var failures = options.Validate();

        failures.Should().Contain(message => message.Contains("must not be a placeholder"));
        failures.Should().NotContain(message => message.Contains(placeholder));
    }

    [Fact]
    public void Validate_ShouldRejectAShortKey()
    {
        var options = new RolePromotionChallengeOptions { SigningKey = "short-random-key" };

        options.Validate().Should().Contain(message => message.Contains("at least 32 characters"));
    }

    [Fact]
    public void Validate_ShouldRejectALowEntropyKey()
    {
        var options = new RolePromotionChallengeOptions
        {
            SigningKey = new string('a', 64)
        };

        options.Validate().Should().Contain(message => message.Contains("enough entropy"));
    }

    [Fact]
    public void Validate_ShouldRejectANegativeCreationCooldown()
    {
        var options = new RolePromotionChallengeOptions
        {
            SigningKey = ValidKey,
            CreationCooldownSeconds = -1
        };

        options.Validate().Should().Contain(message => message.Contains("CreationCooldownSeconds"));
        options.CreationCooldown.Should().Be(TimeSpan.Zero, "the normalized value never becomes negative");
    }

    [Fact]
    public void Validator_ShouldSignalSuccessAndFailure()
    {
        var validator = new RolePromotionChallengeOptionsValidator();

        validator.Validate(null, new RolePromotionChallengeOptions { SigningKey = ValidKey })
            .Succeeded.Should().BeTrue();
        validator.Validate(null, new RolePromotionChallengeOptions { SigningKey = string.Empty })
            .Failed.Should().BeTrue();
    }

    [Fact]
    public void Service_ShouldFailClosed_WhenNoDedicatedKeyIsConfigured()
    {
        using var db = CreateDbContext();
        var options = Options.Create(new RolePromotionChallengeOptions
        {
            SigningKey = "CHANGE_ME_ROLE_PROMOTION_SIGNING_KEY_VALUE"
        });

        var act = () => new RolePromotionChallengeService(
            db,
            options,
            TimeProvider.System,
            NullLogger<RolePromotionChallengeService>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RolePromotionChallenge:SigningKey*")
            .Which.Message.Should().NotContain("CHANGE_ME_ROLE_PROMOTION_SIGNING_KEY_VALUE");
    }

    [Fact]
    public async Task Host_ShouldFailToStart_WhenTheDedicatedKeyIsMissing()
    {
        // Mirrors the AddInfrastructure registration: ValidateOnStart must abort the host instead
        // of starting with an empty/placeholder signing key.
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{RolePromotionChallengeOptions.SectionName}:SigningKey"] = string.Empty
        });

        builder.Services.AddOptions<RolePromotionChallengeOptions>()
            .Bind(builder.Configuration.GetSection(RolePromotionChallengeOptions.SectionName))
            .ValidateOnStart();
        builder.Services
            .AddSingleton<IValidateOptions<RolePromotionChallengeOptions>, RolePromotionChallengeOptionsValidator>();

        using var host = builder.Build();

        var act = () => host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*RolePromotionChallenge:SigningKey*");
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-role-promotion-options-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }
}
