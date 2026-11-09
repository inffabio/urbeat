using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Urbeat.Infrastructure.Persistence.Migrations;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class AddOutboxHandlerDeliveriesMigrationTests
{
    private readonly AddOutboxHandlerDeliveries _migration = new();

    [Fact]
    public void Up_ShouldCreateDeliveryTable_WithRequiredColumns()
    {
        var table = _migration.UpOperations
            .OfType<CreateTableOperation>()
            .Single(x => x.Name == "OutboxHandlerDeliveries");

        table.Columns.Select(x => x.Name).Should().Contain(new[]
        {
            "Id",
            "OutboxMessageId",
            "HandlerKey",
            "Channel",
            "AggregateId",
            "Sequence",
            "Status",
            "AttemptCount",
            "AvailableAtUtc",
            "LockedUntilUtc",
            "ProcessedAtUtc",
            "LastError",
            "CreatedAtUtc"
        });
    }

    [Fact]
    public void Up_ShouldCreateLookupAndUniqueIndexes()
    {
        var indexes = _migration.UpOperations.OfType<CreateIndexOperation>().ToList();

        indexes.Should().Contain(x =>
            x.Table == "OutboxHandlerDeliveries"
            && x.Name == "UX_OutboxHandlerDeliveries_Message_Handler"
            && x.IsUnique
            && x.Columns.SequenceEqual(new[] { "OutboxMessageId", "HandlerKey" }));

        indexes.Should().Contain(x =>
            x.Table == "OutboxHandlerDeliveries"
            && x.Name == "IX_OutboxHandlerDeliveries_Channel_Status_Available_Lock"
            && x.Columns.SequenceEqual(new[] { "Channel", "Status", "AvailableAtUtc", "LockedUntilUtc" }));

        indexes.Should().Contain(x =>
            x.Table == "OutboxHandlerDeliveries"
            && x.Name == "IX_OutboxHandlerDeliveries_Channel_Aggregate_Sequence"
            && x.Columns.SequenceEqual(new[] { "Channel", "AggregateId", "Sequence" }));
    }

    [Fact]
    public void Down_ShouldDropDeliveryTable()
    {
        _migration.DownOperations
            .OfType<DropTableOperation>()
            .Should().Contain(x => x.Name == "OutboxHandlerDeliveries");
    }
}
