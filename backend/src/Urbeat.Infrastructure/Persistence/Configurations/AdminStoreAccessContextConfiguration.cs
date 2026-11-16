using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the short-lived administrative store context. Only the versioned token hash is
/// stored; timestamps are UTC and the store relationship cascades so deleting a store invalidates
/// every delegated context with it.
/// </summary>
public sealed class AdminStoreAccessContextConfiguration : IEntityTypeConfiguration<AdminStoreAccessContext>
{
    public void Configure(EntityTypeBuilder<AdminStoreAccessContext> entity)
    {
        entity.ToTable("AdminStoreAccessContexts");
        entity.HasKey(x => x.Id);

        // Authoritative uniqueness/deduplication for hash lookups.
        entity.HasIndex(x => x.TokenHash).IsUnique();
        entity.HasIndex(x => new { x.AdminUserId, x.ExpiresAtUtc });
        entity.HasIndex(x => x.StoreId);

        entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RevokedReason).HasMaxLength(40);
        entity.Property(x => x.Reason).HasMaxLength(200);
        entity.Property(x => x.IpAddress).HasMaxLength(64);
        entity.Property(x => x.UserAgent).HasMaxLength(256);

        entity.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        entity.Property(x => x.RevokedAtUtc).HasColumnType("timestamp with time zone");
        entity.Property(x => x.LastSeenAtUtc).HasColumnType("timestamp with time zone");

        entity.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
