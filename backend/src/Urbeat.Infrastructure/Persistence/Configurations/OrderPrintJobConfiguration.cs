using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Urbeat.Infrastructure.Persistence.Configurations;

public sealed class OrderPrintJobConfiguration : IEntityTypeConfiguration<OrderPrintJob>
{
    public void Configure(EntityTypeBuilder<OrderPrintJob> builder)
    {
        builder.ToTable("OrderPrintJobs");

        builder.HasKey(x => x.Cursor);
        builder.Property(x => x.Cursor).UseIdentityByDefaultColumn();

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.StoreId, x.Status, x.Cursor })
            .HasDatabaseName("IX_OrderPrintJobs_StoreId_Status_Cursor");

        builder.Property(x => x.Code).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.LastOperationKey).HasMaxLength(320);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
