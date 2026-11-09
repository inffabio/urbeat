using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Npgsql;
using Urbeat.Infrastructure.DependencyInjection;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class NpgsqlPoolConfigurationTests
{
    private const string BaseConnectionString =
        "Host=db;Port=5432;Database=UrbeatDb;Username=postgres;Password=super-secret-value;" +
        "Pooling=true;SSL Mode=Disable;Trust Server Certificate=true";

    [Fact]
    public void ResolveConnectionString_ShouldApplyConservativeDefaults_WhenNothingIsConfigured()
    {
        var builder = Resolve(Configuration(DefaultConnection(BaseConnectionString)));

        builder.MaxPoolSize.Should().Be(NpgsqlPoolConfiguration.DefaultMaximumPoolSize);
        builder.MaxPoolSize.Should().Be(10);
        builder.MinPoolSize.Should().Be(NpgsqlPoolConfiguration.DefaultMinimumPoolSize);
        builder.MinPoolSize.Should().Be(0);
    }

    [Fact]
    public void ResolveConnectionString_ShouldHonorEnvironmentStyleKeys()
    {
        var builder = Resolve(Configuration(
            DefaultConnection(BaseConnectionString),
            (NpgsqlPoolConfiguration.MaximumPoolSizeKey, "6"),
            (NpgsqlPoolConfiguration.MinimumPoolSizeKey, "2")));

        builder.MaxPoolSize.Should().Be(6);
        builder.MinPoolSize.Should().Be(2);
    }

    [Fact]
    public void ResolveConnectionString_ShouldPreferSectionOverEnvironmentStyleKeys()
    {
        var builder = Resolve(Configuration(
            DefaultConnection(BaseConnectionString),
            (NpgsqlPoolConfiguration.MaximumPoolSizeKey, "6"),
            (NpgsqlPoolConfiguration.MaximumPoolSizeSectionKey, "8"),
            (NpgsqlPoolConfiguration.MinimumPoolSizeKey, "1"),
            (NpgsqlPoolConfiguration.MinimumPoolSizeSectionKey, "3")));

        builder.MaxPoolSize.Should().Be(8);
        builder.MinPoolSize.Should().Be(3);
    }

    [Fact]
    public void ResolveConnectionString_ShouldPreserveExplicitPoolSizesFromTheConnectionString()
    {
        var builder = Resolve(Configuration(
            DefaultConnection(BaseConnectionString + ";Maximum Pool Size=25;Minimum Pool Size=4"),
            (NpgsqlPoolConfiguration.MaximumPoolSizeKey, "6")));

        builder.MaxPoolSize.Should().Be(25);
        builder.MinPoolSize.Should().Be(4);
    }

    [Fact]
    public void ResolveConnectionString_ShouldPreserveUnrelatedSettings()
    {
        var builder = Resolve(Configuration(DefaultConnection(BaseConnectionString)));

        builder.Host.Should().Be("db");
        builder.Port.Should().Be(5432);
        builder.Database.Should().Be("UrbeatDb");
        builder.Username.Should().Be("postgres");
        builder.Password.Should().Be("super-secret-value");
        builder.SslMode.Should().Be(SslMode.Disable);
        builder.Pooling.Should().BeTrue();
    }

    [Fact]
    public void ResolveConnectionString_ShouldLeavePoolKeysUntouched_WhenPoolingIsDisabled()
    {
        var builder = Resolve(Configuration(
            DefaultConnection("Host=db;Database=UrbeatDb;Username=postgres;Password=super-secret-value;Pooling=false")));

        builder.Pooling.Should().BeFalse();
        builder.ShouldSerialize("Maximum Pool Size").Should().BeFalse();
        builder.ShouldSerialize("Minimum Pool Size").Should().BeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("abc")]
    public void ResolveConnectionString_ShouldRejectInvalidMaximumPoolSize(string value)
    {
        var act = () => NpgsqlPoolConfiguration.ResolveConnectionString(Configuration(
            DefaultConnection(BaseConnectionString),
            (NpgsqlPoolConfiguration.MaximumPoolSizeKey, value)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{NpgsqlPoolConfiguration.MaximumPoolSizeSectionKey}*");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("11")]
    public void ResolveConnectionString_ShouldRejectInvalidMinimumPoolSize(string value)
    {
        var act = () => NpgsqlPoolConfiguration.ResolveConnectionString(Configuration(
            DefaultConnection(BaseConnectionString),
            (NpgsqlPoolConfiguration.MinimumPoolSizeKey, value)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{NpgsqlPoolConfiguration.MinimumPoolSizeSectionKey}*");
    }

    [Fact]
    public void ResolveConnectionString_ShouldRejectMissingConnectionString()
    {
        var act = () => NpgsqlPoolConfiguration.ResolveConnectionString(Configuration());

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:DefaultConnection*");
    }

    [Fact]
    public void ResolveConnectionString_ShouldNotLeakTheConnectionString_WhenItIsMalformed()
    {
        var act = () => NpgsqlPoolConfiguration.ResolveConnectionString(Configuration(
            DefaultConnection("Host=db;Password=super-secret-value;NotAValidKeyword=1")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a valid Npgsql connection string*")
            .Which.Message.Should().NotContain("super-secret-value");
    }

    [Fact]
    public void AddInfrastructure_ShouldRegisterDbContextOptionsWithExplicitPoolLimits()
    {
        var configuration = Configuration(
            DefaultConnection(BaseConnectionString),
            ("Jwt:Secret", "unit-test-secret-with-at-least-32-chars"),
            (NpgsqlPoolConfiguration.MaximumPoolSizeKey, "7"),
            (NpgsqlPoolConfiguration.MinimumPoolSizeKey, "2"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, new Mock<IHostEnvironment>().Object);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        var connectionString = RelationalOptionsExtension.Extract(options).ConnectionString;

        connectionString.Should().NotBeNull();
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.MaxPoolSize.Should().Be(7);
        builder.MinPoolSize.Should().Be(2);
    }

    private static NpgsqlConnectionStringBuilder Resolve(IConfiguration configuration) =>
        new(NpgsqlPoolConfiguration.ResolveConnectionString(configuration));

    private static (string Key, string? Value) DefaultConnection(string value) =>
        ("ConnectionStrings:DefaultConnection", value);

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();
}
