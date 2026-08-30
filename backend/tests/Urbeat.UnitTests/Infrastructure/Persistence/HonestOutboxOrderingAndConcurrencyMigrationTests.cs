using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Urbeat.Infrastructure.Persistence.Migrations;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class HonestOutboxOrderingAndConcurrencyMigrationTests
{
    private readonly HonestOutboxOrderingAndConcurrency _migration = new();

    [Fact]
    public void Up_ShouldAddSequenceColumn_ToOutboxMessages()
    {
        var addSequence = _migration.UpOperations
            .OfType<AddColumnOperation>()
            .Single(x => x.Table == "OutboxMessages" && x.Name == "Sequence");

        addSequence.ClrType.Should().Be(typeof(long));
        addSequence.IsNullable.Should().BeFalse();
        addSequence.DefaultValue.Should().Be(0L);
    }

    [Fact]
    public void Up_ShouldAddStatusVersionColumn_ToOrders()
    {
        var addStatusVersion = _migration.UpOperations
            .OfType<AddColumnOperation>()
            .Single(x => x.Table == "Orders" && x.Name == "StatusVersion");

        addStatusVersion.ClrType.Should().Be(typeof(int));
        addStatusVersion.IsNullable.Should().BeFalse();
        addStatusVersion.DefaultValue.Should().Be(0);
    }

    [Fact]
    public void Up_ShouldAddConcurrencyStampColumn_ToPayments()
    {
        var addConcurrencyStamp = _migration.UpOperations
            .OfType<AddColumnOperation>()
            .Single(x => x.Table == "Payments" && x.Name == "ConcurrencyStamp");

        addConcurrencyStamp.ClrType.Should().Be(typeof(Guid));
        addConcurrencyStamp.IsNullable.Should().BeFalse();
        addConcurrencyStamp.DefaultValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Up_ShouldCreateSequenceIndex_OnOutboxMessages()
    {
        _migration.UpOperations
            .OfType<CreateIndexOperation>()
            .Should().Contain(x =>
                x.Table == "OutboxMessages"
                && x.Name == "IX_OutboxMessages_AggregateId_Sequence"
                && x.Columns.SequenceEqual(new[] { "AggregateId", "Sequence" }));
    }

    [Fact]
    public void Down_ShouldRemoveAllAddedColumnsAndIndex()
    {
        var down = _migration.DownOperations;

        down.OfType<DropColumnOperation>()
            .Should().Contain(x => x.Table == "OutboxMessages" && x.Name == "Sequence");
        down.OfType<DropColumnOperation>()
            .Should().Contain(x => x.Table == "Orders" && x.Name == "StatusVersion");
        down.OfType<DropColumnOperation>()
            .Should().Contain(x => x.Table == "Payments" && x.Name == "ConcurrencyStamp");
        down.OfType<DropIndexOperation>()
            .Should().Contain(x => x.Name == "IX_OutboxMessages_AggregateId_Sequence");
    }
}
