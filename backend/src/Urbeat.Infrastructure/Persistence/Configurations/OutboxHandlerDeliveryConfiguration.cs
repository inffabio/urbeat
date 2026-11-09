using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Urbeat.Infrastructure.Persistence.Configurations;

public sealed class OutboxHandlerDeliveryConfiguration : IEntityTypeConfiguration<OutboxHandlerDelivery>
{
    public void Configure(EntityTypeBuilder<OutboxHandlerDelivery> builder)
    {
        builder.ToTable("OutboxHandlerDeliveries");

        builder.HasKey(x => x.Id);

        // One delivery per handler per message. Fan-out is idempotent thanks to this constraint.
        builder.HasIndex(x => new { x.OutboxMessageId, x.HandlerKey })
            .IsUnique()
            .HasDatabaseName("UX_OutboxHandlerDeliveries_Message_Handler");

        builder.HasIndex(x => new { x.Channel, x.Status, x.AvailableAtUtc, x.LockedUntilUtc })
            .HasDatabaseName("IX_OutboxHandlerDeliveries_Channel_Status_Available_Lock");

        builder.HasIndex(x => new { x.Channel, x.AggregateId, x.Sequence })
            .HasDatabaseName("IX_OutboxHandlerDeliveries_Channel_Aggregate_Sequence");

        builder.Property(x => x.HandlerKey).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Channel).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.LastError).HasMaxLength(500);

        builder.HasOne<OutboxMessage>()
            .WithMany()
            .HasForeignKey(x => x.OutboxMessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
