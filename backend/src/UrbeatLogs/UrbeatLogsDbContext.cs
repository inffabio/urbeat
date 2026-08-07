using Microsoft.EntityFrameworkCore;

namespace UrbeatLogs;

public class UrbeatLogsDbContext : DbContext
{
    public UrbeatLogsDbContext(DbContextOptions<UrbeatLogsDbContext> options)
        : base(options)
    {
    }

    public DbSet<StructuredLog> StructuredLogs => Set<StructuredLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<StructuredLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.Level).IsRequired();
            entity.Property(e => e.Message).IsRequired();
            entity.Property(e => e.Application).IsRequired();
            entity.Property(e => e.Environment).IsRequired();
            entity.Property(e => e.Endpoint);
            entity.Property(e => e.UserId);
            entity.Property(e => e.StoreId);
            entity.Property(e => e.OrderId);
            entity.Property(e => e.EventType);
            entity.Property(e => e.Exception);
            entity.Property(e => e.CorrelationId);
            entity.Property(e => e.TraceId);
            entity.Property(e => e.Provider);
            entity.Property(e => e.SourceContext);
        });
    }
}
