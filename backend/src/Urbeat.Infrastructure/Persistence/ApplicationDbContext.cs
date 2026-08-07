using Urbeat.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence;

public sealed class ApplicationDbContext
    : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<CuisineType> CuisineTypes => Set<CuisineType>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<StoreAddress> StoreAddresses => Set<StoreAddress>();

    public DbSet<StoreBusinessHour> StoreBusinessHours => Set<StoreBusinessHour>();

    public DbSet<StoreDeliveryArea> StoreDeliveryAreas => Set<StoreDeliveryArea>();

    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    public DbSet<CustomerPhoneVerification> CustomerPhoneVerifications => Set<CustomerPhoneVerification>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();

    public DbSet<PaymentStatusHistory> PaymentStatusHistories => Set<PaymentStatusHistory>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<SellerSubscriptionStatus> SellerSubscriptionStatuses => Set<SellerSubscriptionStatus>();

    public DbSet<SellerSubscription> SellerSubscriptions => Set<SellerSubscription>();

    public DbSet<SellerSubscriptionChargeHistory> SellerSubscriptionChargeHistories => Set<SellerSubscriptionChargeHistory>();

    public DbSet<SubscriptionWebhookEvent> SubscriptionWebhookEvents => Set<SubscriptionWebhookEvent>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<OrderReview> OrderReviews => Set<OrderReview>();

    public DbSet<SystemParameter> SystemParameters => Set<SystemParameter>();

    public DbSet<StorePaymentGatewayConfig> StorePaymentGatewayConfigs => Set<StorePaymentGatewayConfig>();

    public DbSet<PaymentGatewayTransactionLog> PaymentGatewayTransactionLogs => Set<PaymentGatewayTransactionLog>();

    public DbSet<LandingPageContent> LandingPageContents => Set<LandingPageContent>();

    public DbSet<ProductOptionGroup> ProductOptionGroups => Set<ProductOptionGroup>();
    public DbSet<ProductOptionItem> ProductOptionItems => Set<ProductOptionItem>();
    public DbSet<StoreAdditionalGroup> StoreAdditionalGroups => Set<StoreAdditionalGroup>();
    public DbSet<StoreAdditional> StoreAdditionals => Set<StoreAdditional>();
    public DbSet<ProductAdditionalAssignment> ProductAdditionalAssignments => Set<ProductAdditionalAssignment>();
    public DbSet<StoreCustomer> StoreCustomers => Set<StoreCustomer>();

    public DbSet<DeliveryNeighborhood> DeliveryNeighborhoods => Set<DeliveryNeighborhood>();

    public DbSet<City> Cities => Set<City>();

    public DbSet<PrinterPreset> PrinterPresets => Set<PrinterPreset>();

    public DbSet<StorePrinterConfig> StorePrinterConfigs => Set<StorePrinterConfig>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Token).IsUnique();
            entity.Property(x => x.Token).HasMaxLength(200);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Event).HasMaxLength(150);
            entity.Property(x => x.Entity).HasMaxLength(150);
        });

        builder.Entity<Store>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OwnerUserId).IsUnique();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Slug).HasMaxLength(120);
            entity.Property(x => x.PhoneNumber).HasMaxLength(20);
            entity.Property(x => x.Document).HasMaxLength(14);
            entity.Property(x => x.PixKey).HasMaxLength(50);
            entity.Property(x => x.InstagramUrl).HasMaxLength(500);
            entity.Property(x => x.FacebookUrl).HasMaxLength(500);
            entity.Property(x => x.TikTokUrl).HasMaxLength(500);
            entity.Property(x => x.WebsiteUrl).HasMaxLength(500);
            entity.Property(x => x.Description).HasMaxLength(300);
            
            entity.Property(x => x.BannerUrl).HasMaxLength(500);
            entity.Property(x => x.LogoUrl).HasMaxLength(500);
            entity.Property(x => x.DeliveryFee).HasPrecision(10, 2);
            entity.Property(x => x.MinimumOrderValue).HasPrecision(10, 2);
            entity.Property(x => x.FreeShippingThreshold).HasPrecision(10, 2);
        });

        builder.Entity<CuisineType>(entity =>
        {
            var cuisineTypeSeedCreatedAtUtc = new DateTime(2026, 7, 28, 23, 47, 40, 0, DateTimeKind.Utc);

            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(80);

            entity.HasData(
                new { Id = new Guid("b1000000-0000-0000-0000-000000000001"), Name = "Acaiteria", IsActive = true, CreatedAtUtc = cuisineTypeSeedCreatedAtUtc },
                new { Id = new Guid("b1000000-0000-0000-0000-000000000002"), Name = "Cachorro Quente", IsActive = true, CreatedAtUtc = cuisineTypeSeedCreatedAtUtc },
                new { Id = new Guid("b1000000-0000-0000-0000-000000000003"), Name = "Comida Arabe", IsActive = true, CreatedAtUtc = cuisineTypeSeedCreatedAtUtc },
                new { Id = new Guid("b1000000-0000-0000-0000-000000000004"), Name = "Comida Japonesa", IsActive = true, CreatedAtUtc = cuisineTypeSeedCreatedAtUtc },
                new { Id = new Guid("b1000000-0000-0000-0000-000000000005"), Name = "Hamburgueria", IsActive = true, CreatedAtUtc = cuisineTypeSeedCreatedAtUtc },
                new { Id = new Guid("b1000000-0000-0000-0000-000000000006"), Name = "Lanches", IsActive = true, CreatedAtUtc = cuisineTypeSeedCreatedAtUtc },
                new { Id = new Guid("b1000000-0000-0000-0000-000000000007"), Name = "Pizzaria", IsActive = true, CreatedAtUtc = cuisineTypeSeedCreatedAtUtc },
                new { Id = new Guid("b1000000-0000-0000-0000-000000000008"), Name = "Tapioca e crepes", IsActive = true, CreatedAtUtc = cuisineTypeSeedCreatedAtUtc }
            );
        });

        builder.Entity<DeliveryTime>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Plan>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Amount).HasPrecision(10, 2);
        });

        builder.Entity<StoreAddress>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.StoreId).IsUnique();
            entity.Property(x => x.Street).HasMaxLength(120);
            entity.Property(x => x.Number).HasMaxLength(20);
            entity.Property(x => x.Neighborhood).HasMaxLength(80);
            entity.Property(x => x.City).HasMaxLength(80);
            entity.Property(x => x.State).HasMaxLength(2);
            entity.Property(x => x.ZipCode).HasMaxLength(12);
            entity.Property(x => x.Complement).HasMaxLength(120);
            entity.Property(x => x.Reference).HasMaxLength(200);
            entity.HasOne<Store>()
                .WithOne()
                .HasForeignKey<StoreAddress>(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StoreDeliveryArea>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Neighborhood).HasMaxLength(80);
            entity.Property(x => x.DeliveryFee).HasPrecision(10, 2);
            entity.Property(x => x.MinimumOrderValue).HasPrecision(10, 2);
            entity.Property(x => x.FreeShippingThreshold).HasPrecision(10, 2);
            entity.Property(x => x.Notes).HasMaxLength(100);
            entity.HasOne<Store>()
                .WithMany(x => x.DeliveryAreas)
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StoreBusinessHour>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StoreId, x.DayOfWeek }).IsUnique();
            entity.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Shifts)
                .WithOne()
                .HasForeignKey(x => x.StoreBusinessHourId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StoreBusinessHourShift>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StartTime).HasColumnType("time");
            entity.Property(x => x.EndTime).HasColumnType("time");
        });

        builder.Entity<CustomerAddress>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId);
            entity.Property(x => x.Cep).HasMaxLength(8);
            entity.Property(x => x.Street).HasMaxLength(120);
            entity.Property(x => x.Number).HasMaxLength(20);
            entity.Property(x => x.Neighborhood).HasMaxLength(80);
            entity.Property(x => x.City).HasMaxLength(80);
            entity.Property(x => x.State).HasMaxLength(2);
            entity.Property(x => x.Complement).HasMaxLength(120);
            entity.Property(x => x.Reference).HasMaxLength(200);
        });

        builder.Entity<CustomerPhoneVerification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.StoreId);
            entity.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.PendingCep).HasMaxLength(8).IsRequired();
            entity.Property(x => x.PendingStreet).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PendingNumber).HasMaxLength(20).IsRequired();
            entity.Property(x => x.PendingComplement).HasMaxLength(120);
            entity.Property(x => x.PendingNeighborhood).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PendingCity).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PendingState).HasMaxLength(2).IsRequired();
            entity.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Order>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.CustomerUserId);
            entity.HasIndex(x => x.StoreId);
            entity.HasIndex(x => new { x.StoreId, x.Status, x.CreatedAtUtc });
            entity.Property(x => x.FulfillmentType)
                .HasConversion<int>();
            entity.Property(x => x.AddressCep).HasMaxLength(8);
            entity.Property(x => x.AddressStreet).HasMaxLength(120);
            entity.Property(x => x.AddressNumber).HasMaxLength(20);
            entity.Property(x => x.AddressNeighborhood).HasMaxLength(80);
            entity.Property(x => x.AddressCity).HasMaxLength(80);
            entity.Property(x => x.AddressState).HasMaxLength(2);
            entity.Property(x => x.AddressComplement).HasMaxLength(120);
            entity.Property(x => x.AddressReference).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.Property(x => x.Subtotal).HasPrecision(10, 2);
            entity.Property(x => x.DeliveryFee).HasPrecision(10, 2);
            entity.Property(x => x.Total).HasPrecision(10, 2);
        });

        builder.Entity<OrderReview>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId).IsUnique();
            entity.HasIndex(x => x.StoreId);
            entity.Property(x => x.Rating).IsRequired();
            entity.Property(x => x.Comment).HasMaxLength(500);
        });

        builder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId);
            entity.Property(x => x.ProductName).HasMaxLength(120);
            entity.Property(x => x.UnitPrice).HasPrecision(10, 2);
            entity.Property(x => x.TotalPrice).HasPrecision(10, 2);
            entity.HasOne<Order>()
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderStatusHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasOne<Order>()
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Payment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId).IsUnique();
            entity.Property(x => x.GatewayTransactionId).HasMaxLength(100);
            entity.Property(x => x.GatewayCheckoutUrl).HasMaxLength(500);
            entity.Property(x => x.ExternalReference).HasMaxLength(100);
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.Property(x => x.RawPayload).HasColumnType("text");
            entity.HasOne<Order>()
                .WithOne()
                .HasForeignKey<Payment>(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PaymentWebhookEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Gateway, x.EventKey }).IsUnique();
            entity.Property(x => x.EventKey).HasMaxLength(150);
            entity.Property(x => x.GatewayTransactionId).HasMaxLength(100);
            entity.Property(x => x.Payload).HasColumnType("text");
        });

        builder.Entity<PaymentStatusHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PaymentId);
            entity.HasIndex(x => new { x.PaymentId, x.NewStatus, x.Source });
            entity.Property(x => x.Source).HasMaxLength(80);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.Property(x => x.RawPayload).HasColumnType("text");
            entity.HasOne<Payment>()
                .WithMany()
                .HasForeignKey(x => x.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Notification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RecipientUserId);
            entity.HasIndex(x => new { x.RecipientUserId, x.OrderId, x.Type }).IsUnique();
            entity.Property(x => x.Title).HasMaxLength(120);
            entity.Property(x => x.Message).HasMaxLength(500);
        });

        builder.Entity<SellerSubscriptionStatus>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SellerUserId).IsUnique();
            entity.HasIndex(x => x.NextDueDateUtc);
        });

        builder.Entity<SellerSubscription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.StoreId).IsUnique();
            entity.HasIndex(x => x.SellerUserId).IsUnique();
            entity.Property(x => x.PlanName).HasMaxLength(120);
            entity.Property(x => x.PlanAmount).HasPrecision(10, 2);
            entity.Property(x => x.GatewayCustomerId).HasMaxLength(120);
            entity.Property(x => x.GatewaySubscriptionId).HasMaxLength(120);
        });

        builder.Entity<SellerSubscriptionChargeHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SellerUserId);
            entity.HasIndex(x => x.DueDateUtc);
            entity.HasIndex(x => x.GatewayChargeId).IsUnique();
            entity.Property(x => x.GatewayChargeId).HasMaxLength(120);
            entity.Property(x => x.ExternalReference).HasMaxLength(120);
            entity.Property(x => x.GatewayStatus).HasMaxLength(50);
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.Property(x => x.RawPayload).HasColumnType("text");
        });

        builder.Entity<SubscriptionWebhookEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventKey).IsUnique();
            entity.HasIndex(x => x.SellerUserId);
            entity.Property(x => x.EventKey).HasMaxLength(200);
            entity.Property(x => x.EventType).HasMaxLength(80);
            entity.Property(x => x.ExternalReference).HasMaxLength(120);
            entity.Property(x => x.Payload).HasColumnType("text");
        });

        builder.Entity<ProductCategory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(80);
            entity.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.StoreId);
            entity.HasIndex(x => x.CategoryId);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Price).HasPrecision(10, 2);
            entity.Property(x => x.ImageUrl).HasMaxLength(500);
            entity.Property(x => x.StockQuantity).HasDefaultValue(0);
            entity.Property(x => x.SaleMode).HasMaxLength(20).HasDefaultValue("single");
            entity.HasOne(x => x.WeightConfig)
                .WithOne()
                .HasForeignKey<ProductWeightConfig>(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ProductCategory>()
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StoreAdditionalGroup>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.HasOne(x => x.Store)
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StoreCustomer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StoreId, x.CustomerUserId }).IsUnique();
            entity.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(x => x.CustomerUserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StoreAdditional>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StoreId, x.GroupId, x.Name }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(300);
            entity.Property(x => x.Price).HasPrecision(10, 2);
            entity.HasOne(x => x.Store)
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Group)
                .WithMany(x => x.Additionals)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductAdditionalAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProductId, x.AdditionalId }).IsUnique();
            entity.HasOne(x => x.Product)
                .WithMany(x => x.AdditionalAssignments)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Additional)
                .WithMany(x => x.ProductAssignments)
                .HasForeignKey(x => x.AdditionalId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductAdditional>(entity =>
        {
            entity.HasOne(x => x.StoreAdditional)
                .WithMany()
                .HasForeignKey(x => x.StoreAdditionalId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ProductWeightConfig>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ProductId).IsUnique();
            entity.Property(x => x.PricePerKg).HasPrecision(10, 2);
        });

        builder.Entity<ProductVariation>(entity =>
        {
            entity.Property(x => x.Description).HasMaxLength(150);
        });

        builder.Entity<SystemParameter>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Group).HasMaxLength(100);
            entity.Property(x => x.Type)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        builder.Entity<StorePaymentGatewayConfig>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StoreId, x.Gateway }).IsUnique();
            entity.Property(x => x.EncryptedAccessToken).HasMaxLength(500);
            entity.Property(x => x.EncryptedNotificationUrl).HasMaxLength(500);
            entity.Property(x => x.Environment).HasMaxLength(20).HasDefaultValue("Sandbox");
            entity.Property(x => x.Gateway).HasConversion<int>();
            entity.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PaymentGatewayTransactionLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId);
            entity.HasIndex(x => x.StoreId);
            entity.HasIndex(x => x.PaymentId);
            entity.Property(x => x.Action).HasMaxLength(80);
            entity.Property(x => x.CorrelationId).HasMaxLength(100);
            entity.Property(x => x.ErrorMessage).HasMaxLength(500);
            entity.Property(x => x.Gateway).HasConversion<int>();
            entity.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LandingPageContent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Section, x.Key }).IsUnique();
            entity.Property(x => x.Section).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Key).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
        });

        builder.Entity<DeliveryNeighborhood>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Neighborhood).HasMaxLength(80).IsRequired();
            entity.Property(x => x.City).HasMaxLength(80).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.CityId).IsRequired(false);
            entity.Property(x => x.OsmId).HasMaxLength(50);
            entity.Property(x => x.OsmType).HasMaxLength(30);
            entity.Property(x => x.PlaceType).HasMaxLength(50);
            entity.Property(x => x.Boundary).HasMaxLength(50);
            entity.Property(x => x.AdminLevel).HasMaxLength(10);
            entity.Property(x => x.Source).HasMaxLength(50);
            entity.HasIndex(x => new { x.Neighborhood, x.City }).IsUnique();
            entity.HasIndex(x => new { x.CityId, x.NormalizedName }).IsUnique().HasFilter("\"CityId\" IS NOT NULL");
            entity.HasOne(x => x.CityEntity)
                .WithMany()
                .HasForeignKey(x => x.CityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<City>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Uf).HasMaxLength(2).IsRequired();
            entity.Property(x => x.IbgeCode).HasMaxLength(20);
            entity.Property(x => x.OsmId).HasMaxLength(50);
            entity.Property(x => x.OsmAreaId).HasMaxLength(50);
            entity.HasIndex(x => new { x.Uf, x.IbgeCode }).IsUnique().HasFilter("\"IbgeCode\" IS NOT NULL");
            entity.HasIndex(x => new { x.Name, x.Uf }).IsUnique();
        });

        builder.Entity<PrinterPreset>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.AdapterId);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Manufacturer).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ConnectionType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PaperWidth).HasMaxLength(10).IsRequired();
            entity.Property(x => x.CommandSet).HasMaxLength(20).IsRequired();
            entity.Property(x => x.AdapterId).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);

            entity.HasData(
                new
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000001"),
                    Name = "Mini Thermal Printer TC-163",
                    Manufacturer = "Havendo",
                    ConnectionType = "android-bluetooth",
                    PaperWidth = "58mm",
                    CommandSet = "esc-pos",
                    AdapterId = "escpos-bluetooth",
                    Description = "Impressora termica chinesa compacta 58mm. Bluetooth classico (SPP) via app Android/Capacitor.",
                    IsActive = true,
                    CreatedAtUtc = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc)
                },
                new
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000002"),
                    Name = "ESC/POS 58mm generica",
                    Manufacturer = "Generica",
                    ConnectionType = "android-bluetooth",
                    PaperWidth = "58mm",
                    CommandSet = "esc-pos",
                    AdapterId = "escpos-bluetooth",
                    Description = "Modelo base para impressoras termicas 58mm compativeis com ESC/POS.",
                    IsActive = true,
                    CreatedAtUtc = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc)
                },
                new
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000003"),
                    Name = "ESC/POS 80mm generica",
                    Manufacturer = "Generica",
                    ConnectionType = "android-bluetooth",
                    PaperWidth = "80mm",
                    CommandSet = "esc-pos",
                    AdapterId = "escpos-bluetooth",
                    Description = "Modelo base para impressoras termicas 80mm compativeis com ESC/POS.",
                    IsActive = true,
                    CreatedAtUtc = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc)
                },
                new
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000004"),
                    Name = "Impressora do navegador",
                    Manufacturer = "Sistema",
                    ConnectionType = "browser-print",
                    PaperWidth = "80mm",
                    CommandSet = "browser",
                    AdapterId = "browser-print",
                    Description = "Usa a janela de impressao do navegador. Dispensa hardware dedicado.",
                    IsActive = true,
                    CreatedAtUtc = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc)
                },
                new
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000005"),
                    Name = "ESC/POS Wi-Fi 58mm",
                    Manufacturer = "Generica",
                    ConnectionType = "wifi",
                    PaperWidth = "58mm",
                    CommandSet = "esc-pos",
                    AdapterId = "wifi-escpos",
                    Description = "Impressora termica 58mm via Wi-Fi (rede local). Mesmos comandos ESC/POS, conexao por IP. Funciona em Android, iOS, Windows e macOS.",
                    IsActive = true,
                    CreatedAtUtc = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        });

        builder.Entity<StorePrinterConfig>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.StoreId).IsUnique();
            entity.Property(x => x.PrinterName).HasMaxLength(120);
            entity.Property(x => x.MacAddress).HasMaxLength(50);
            entity.Property(x => x.FooterText).HasMaxLength(200);
        });

    }
}



