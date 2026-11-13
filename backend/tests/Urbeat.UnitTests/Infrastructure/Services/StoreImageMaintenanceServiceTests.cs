using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Xunit;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class StoreImageMaintenanceServiceTests : IDisposable
{
    private const string CloudinaryBaseUrl = "https://res.cloudinary.com/urbeat/image/upload/v1712345678/";

    private readonly ApplicationDbContext _db;
    private readonly FakeCloudinaryImageCatalog _catalog = new();
    private readonly StoreImageMaintenanceService _sut;

    public StoreImageMaintenanceServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-cloudinary-maintenance-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        _sut = new StoreImageMaintenanceService(
            _db,
            _catalog,
            Options.Create(new CloudinaryMaintenanceOptions()),
            NullLogger<StoreImageMaintenanceService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ScanAsync_ShouldNeverDelete_AndShouldProtectReferencedRecentAndUnknownAgeAssets()
    {
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja",
            Slug = "loja",
            LogoUrl = CloudinaryBaseUrl + "stores/loja/logo/referenced.png"
        };
        _db.Stores.Add(store);
        _db.Products.Add(new Product
        {
            StoreId = store.Id,
            CategoryId = Guid.NewGuid(),
            Name = "Produto",
            ImageUrl = CloudinaryBaseUrl + "stores/loja/products/referenced-product.png"
        });
        _db.LandingPageContents.Add(new LandingPageContent
        {
            Section = "Hero",
            Key = "Preview",
            Value = $"<img src=\"{CloudinaryBaseUrl}stores/loja/store-media/landing-preview.png\" />"
        });
        await _db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        _catalog.Resources.AddRange(
            Resource("stores/loja/logo/referenced", now.AddDays(-3)),
            Resource("stores/loja/products/referenced-product", now.AddDays(-3)),
            Resource("stores/loja/store-media/landing-preview", now.AddDays(-3)),
            Resource("stores/loja/products/orphan-old", now.AddDays(-3)),
            Resource("stores/loja/products/orphan-recent", now.AddMinutes(-5)),
            new CloudinaryImageResourceDto("stores/_pending/user-id/logo/orphan-old", now.AddDays(-3), "png"),
            new CloudinaryImageResourceDto("stores/loja/products/unknown-age", null, "png"),
            new CloudinaryImageResourceDto("outside/prefix/orphan", now.AddDays(-3), "png"));

        var scan = await _sut.ScanAsync(new StoreImageOrphanScanRequestDto { MinimumAgeMinutes = 60 });

        scan.Prefix.Should().Be("stores/");
        scan.ScannedCount.Should().Be(7, "the fake catalog filters by the requested prefix");
        scan.ReferencedCount.Should().Be(3);
        scan.RecentCount.Should().Be(1);
        scan.UnknownAgeCount.Should().Be(1);
        scan.OutsidePrefixCount.Should().Be(0);
        scan.Orphans.Select(x => x.PublicId).Should().BeEquivalentTo(new[]
        {
            "stores/loja/products/orphan-old",
            "stores/_pending/user-id/logo/orphan-old"
        });
        scan.Orphans.Single(x => x.PublicId.Contains("_pending")).IsPending.Should().BeTrue();
        _catalog.DestroyedPublicIds.Should().BeEmpty("a scan is a dry run");
    }

    [Fact]
    public async Task CleanupAsync_ShouldDeleteOnlyConfirmedOrphans()
    {
        var now = DateTime.UtcNow;
        _db.Stores.Add(new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja",
            Slug = "loja",
            LogoUrl = CloudinaryBaseUrl + "stores/loja/logo/referenced.png"
        });
        await _db.SaveChangesAsync();

        _catalog.Resources.AddRange(
            Resource("stores/loja/logo/referenced", now.AddDays(-3)),
            Resource("stores/loja/products/orphan", now.AddDays(-3)),
            Resource("stores/loja/products/recent", now.AddMinutes(-5)),
            new CloudinaryImageResourceDto("stores/_pending/user/logo/pending-old", now.AddDays(-3), "png"));

        var result = await _sut.CleanupAsync(new StoreImageOrphanCleanupRequestDto
        {
            Confirm = true,
            MinimumAgeMinutes = 60
        });

        result.ConfirmationRequired.Should().BeFalse();
        result.DeletedCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        _catalog.DestroyedPublicIds.Should().BeEquivalentTo(new[]
        {
            "stores/loja/products/orphan",
            "stores/_pending/user/logo/pending-old"
        });
    }

    [Fact]
    public async Task CleanupAsync_ShouldNotContactCloudinary_WhenConfirmationIsMissing()
    {
        _catalog.Resources.Add(Resource("stores/loja/products/orphan", DateTime.UtcNow.AddDays(-3)));

        var result = await _sut.CleanupAsync(new StoreImageOrphanCleanupRequestDto
        {
            Confirm = false,
            MinimumAgeMinutes = 60
        });

        result.ConfirmationRequired.Should().BeTrue();
        result.DeletedCount.Should().Be(0);
        _catalog.ListCalls.Should().Be(0);
        _catalog.DestroyedPublicIds.Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupAsync_ShouldContinueTheBatch_WhenOneDeletionFails()
    {
        var now = DateTime.UtcNow;
        _catalog.Resources.AddRange(
            Resource("stores/loja/products/fails", now.AddDays(-3)),
            Resource("stores/loja/products/succeeds", now.AddDays(-3)));
        _catalog.FailForPublicIds.Add("stores/loja/products/fails");

        var result = await _sut.CleanupAsync(new StoreImageOrphanCleanupRequestDto
        {
            Confirm = true,
            MinimumAgeMinutes = 60
        });

        result.DeletedCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Failures.Single().PublicId.Should().Be("stores/loja/products/fails");
        _catalog.DestroyedPublicIds.Should().ContainSingle()
            .Which.Should().Be("stores/loja/products/succeeds");
    }

    [Fact]
    public async Task ScanAsync_ShouldPaginateAndReportTruncation()
    {
        var now = DateTime.UtcNow.AddDays(-3);
        for (var index = 0; index < 5; index++)
        {
            _catalog.Resources.Add(Resource($"stores/loja/products/orphan-{index}", now));
        }

        var scan = await _sut.ScanAsync(new StoreImageOrphanScanRequestDto
        {
            MaximumAssets = 2,
            MinimumAgeMinutes = 60
        });

        scan.ScannedCount.Should().Be(2);
        scan.Orphans.Should().HaveCount(2);
        scan.Truncated.Should().BeTrue();
        _catalog.ListCalls.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task CleanupAsync_ShouldRespectMaximumDeletions()
    {
        var now = DateTime.UtcNow.AddDays(-3);
        for (var index = 0; index < 5; index++)
        {
            _catalog.Resources.Add(Resource($"stores/loja/products/orphan-{index}", now));
        }

        var result = await _sut.CleanupAsync(new StoreImageOrphanCleanupRequestDto
        {
            Confirm = true,
            MinimumAgeMinutes = 60,
            MaximumDeletions = 2
        });

        result.DeletedCount.Should().Be(2);
        _catalog.DestroyedPublicIds.Should().HaveCount(2);
        result.OrphanCount.Should().Be(5);
    }

    private static CloudinaryImageResourceDto Resource(string publicId, DateTime createdAtUtc)
        => new(publicId, createdAtUtc, "png");

    private sealed class FakeCloudinaryImageCatalog : ICloudinaryImageCatalog
    {
        public List<CloudinaryImageResourceDto> Resources { get; } = new();

        public List<string> DestroyedPublicIds { get; } = new();

        public HashSet<string> FailForPublicIds { get; } = new(StringComparer.Ordinal);

        public int ListCalls { get; private set; }

        public Task<CloudinaryImagePageDto> ListImagesAsync(
            string prefix,
            int maxResults,
            string? nextCursor,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;

            var filtered = Resources
                .Where(resource => resource.PublicId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var start = int.TryParse(nextCursor, out var parsed) ? parsed : 0;
            var page = filtered.Skip(start).Take(maxResults).ToList();
            var nextIndex = start + page.Count;

            return Task.FromResult(new CloudinaryImagePageDto
            {
                Resources = page,
                NextCursor = nextIndex < filtered.Count ? nextIndex.ToString() : null
            });
        }

        public Task<bool> DestroyAsync(string publicId, CancellationToken cancellationToken = default)
        {
            if (FailForPublicIds.Contains(publicId))
            {
                throw new InvalidOperationException("Cloudinary deletion failed.");
            }

            DestroyedPublicIds.Add(publicId);
            return Task.FromResult(true);
        }
    }
}
