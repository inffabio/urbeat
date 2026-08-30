using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Urbeat.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.Status, x.AvailableAtUtc, x.LockedUntilUtc })
            .HasDatabaseName("IX_OutboxMessages_Status_AvailableAt_LockedUntil");

        builder.HasIndex(x => new { x.AggregateId, x.Type })
            .HasDatabaseName("IX_OutboxMessages_AggregateId_Type");

        builder.HasIndex(x => new { x.AggregateId, x.Sequence })
            .HasDatabaseName("IX_OutboxMessages_AggregateId_Sequence");

        builder.Property(x => x.Type).HasMaxLength(120).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(120);
        builder.Property(x => x.Payload).HasColumnType("text").IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.Status).HasConversion<int>();
    }
}
