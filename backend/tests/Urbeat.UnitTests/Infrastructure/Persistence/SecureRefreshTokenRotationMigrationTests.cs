using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Urbeat.Infrastructure.Persistence.Migrations;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class SecureRefreshTokenRotationMigrationTests
{
    [Fact]
    public void Up_ShouldStillPurgeLegacyPlaintextTokens()
    {
        var migration = new SecureRefreshTokenRotationAndPaymentAttempt();

        migration.UpOperations
            .OfType<SqlOperation>()
            .Should().Contain(x => x.Sql.Contains("DELETE FROM \"RefreshTokens\"", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Down_ShouldNotCreateUniqueIndexOnEmptyDefaultToken()
    {
        var migration = new SecureRefreshTokenRotationAndPaymentAttempt();
        var down = migration.DownOperations;

        var addToken = down.OfType<AddColumnOperation>()
            .Single(x => x.Table == "RefreshTokens" && x.Name == "Token");

        addToken.IsNullable.Should().BeTrue();
        addToken.DefaultValue.Should().BeNull();

        down.OfType<SqlOperation>()
            .Should().Contain(x => x.Sql.Contains("UPDATE \"RefreshTokens\" SET \"Token\"", System.StringComparison.Ordinal));

        var alterToken = down.OfType<AlterColumnOperation>()
            .Single(x => x.Table == "RefreshTokens" && x.Name == "Token");

        alterToken.IsNullable.Should().BeFalse();

        down.OfType<CreateIndexOperation>()
            .Should().Contain(x =>
                x.Table == "RefreshTokens"
                && x.Name == "IX_RefreshTokens_Token"
                && x.IsUnique);
    }
}
