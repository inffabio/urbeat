using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Urbeat.Infrastructure.Identity;

namespace Urbeat.UnitTests.Infrastructure.Identity;

public sealed class Argon2idOptionsTests
{
    [Fact]
    public void Defaults_ShouldMatchTheApprovedProductionProfile()
    {
        var options = new Argon2idPasswordHasherOptions();

        options.MemorySizeKb.Should().Be(65_536);
        options.Iterations.Should().Be(3);
        options.DegreeOfParallelism.Should().Be(2);
        options.SaltSizeBytes.Should().Be(16);
        options.HashSizeBytes.Should().Be(32);
        options.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Options_ShouldNotExposeAnyPepperSetting()
    {
        typeof(Argon2idPasswordHasherOptions)
            .GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("Pepper", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(AlgError.MemoryTooSmall)]
    [InlineData(AlgError.MemoryTooLarge)]
    [InlineData(AlgError.IterationsTooSmall)]
    [InlineData(AlgError.IterationsTooLarge)]
    [InlineData(AlgError.ParallelismTooSmall)]
    [InlineData(AlgError.ParallelismTooLarge)]
    [InlineData(AlgError.SaltTooSmall)]
    [InlineData(AlgError.SaltTooLarge)]
    [InlineData(AlgError.HashTooSmall)]
    [InlineData(AlgError.HashTooLarge)]
    public void Validate_ShouldRejectOutOfBoundsValues(AlgError error)
    {
        var options = InvalidOptions(error);

        options.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_ShouldAcceptTheConfiguredBounds()
    {
        var options = new Argon2idPasswordHasherOptions
        {
            MemorySizeKb = Argon2idPasswordHasherOptions.MinimumMemorySizeKb,
            Iterations = Argon2idPasswordHasherOptions.MinimumIterations,
            DegreeOfParallelism = Argon2idPasswordHasherOptions.MinimumDegreeOfParallelism,
            SaltSizeBytes = Argon2idPasswordHasherOptions.MinimumSaltSizeBytes,
            HashSizeBytes = Argon2idPasswordHasherOptions.MinimumHashSizeBytes
        };

        options.Validate().Should().BeEmpty();

        options = new Argon2idPasswordHasherOptions
        {
            MemorySizeKb = Argon2idPasswordHasherOptions.MaximumMemorySizeKb,
            Iterations = Argon2idPasswordHasherOptions.MaximumIterations,
            DegreeOfParallelism = Argon2idPasswordHasherOptions.MaximumDegreeOfParallelism,
            SaltSizeBytes = Argon2idPasswordHasherOptions.MaximumSaltSizeBytes,
            HashSizeBytes = Argon2idPasswordHasherOptions.MaximumHashSizeBytes
        };

        options.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Options_ShouldBindFromNonSecretConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{Argon2idPasswordHasherOptions.SectionName}:MemorySizeKb"] = "32768",
                [$"{Argon2idPasswordHasherOptions.SectionName}:Iterations"] = "4",
                [$"{Argon2idPasswordHasherOptions.SectionName}:DegreeOfParallelism"] = "2",
                [$"{Argon2idPasswordHasherOptions.SectionName}:SaltSizeBytes"] = "16",
                [$"{Argon2idPasswordHasherOptions.SectionName}:HashSizeBytes"] = "32"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<Argon2idPasswordHasherOptions>()
            .Bind(configuration.GetSection(Argon2idPasswordHasherOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Argon2idPasswordHasherOptions>, Argon2idPasswordHasherOptionsValidator>();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<Argon2idPasswordHasherOptions>>().Value;

        Argon2idPasswordHasherOptions.SectionName.Should().Be("PasswordHashing:Argon2id");
        options.MemorySizeKb.Should().Be(32_768);
        options.Iterations.Should().Be(4);
        options.DegreeOfParallelism.Should().Be(2);
        options.SaltSizeBytes.Should().Be(16);
        options.HashSizeBytes.Should().Be(32);
    }

    [Fact]
    public void Validator_ShouldFailForOutOfBoundsConfiguration()
    {
        var validator = new Argon2idPasswordHasherOptionsValidator();
        var options = InvalidOptions(AlgError.MemoryTooLarge);

        var result = validator.Validate(Options.DefaultName, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("MemorySizeKb", StringComparison.Ordinal));
    }

    public enum AlgError
    {
        MemoryTooSmall,
        MemoryTooLarge,
        IterationsTooSmall,
        IterationsTooLarge,
        ParallelismTooSmall,
        ParallelismTooLarge,
        SaltTooSmall,
        SaltTooLarge,
        HashTooSmall,
        HashTooLarge
    }

    private static Argon2idPasswordHasherOptions InvalidOptions(AlgError error)
    {
        var options = new Argon2idPasswordHasherOptions();
        switch (error)
        {
            case AlgError.MemoryTooSmall:
                options.MemorySizeKb = Argon2idPasswordHasherOptions.MinimumMemorySizeKb - 1;
                break;
            case AlgError.MemoryTooLarge:
                options.MemorySizeKb = Argon2idPasswordHasherOptions.MaximumMemorySizeKb + 1;
                break;
            case AlgError.IterationsTooSmall:
                options.Iterations = Argon2idPasswordHasherOptions.MinimumIterations - 1;
                break;
            case AlgError.IterationsTooLarge:
                options.Iterations = Argon2idPasswordHasherOptions.MaximumIterations + 1;
                break;
            case AlgError.ParallelismTooSmall:
                options.DegreeOfParallelism = Argon2idPasswordHasherOptions.MinimumDegreeOfParallelism - 1;
                break;
            case AlgError.ParallelismTooLarge:
                options.DegreeOfParallelism = Argon2idPasswordHasherOptions.MaximumDegreeOfParallelism + 1;
                break;
            case AlgError.SaltTooSmall:
                options.SaltSizeBytes = Argon2idPasswordHasherOptions.MinimumSaltSizeBytes - 1;
                break;
            case AlgError.SaltTooLarge:
                options.SaltSizeBytes = Argon2idPasswordHasherOptions.MaximumSaltSizeBytes + 1;
                break;
            case AlgError.HashTooSmall:
                options.HashSizeBytes = Argon2idPasswordHasherOptions.MinimumHashSizeBytes - 1;
                break;
            case AlgError.HashTooLarge:
                options.HashSizeBytes = Argon2idPasswordHasherOptions.MaximumHashSizeBytes + 1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(error), error, null);
        }

        return options;
    }
}
