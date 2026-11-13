using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Finds Cloudinary images under <c>stores/</c> that are not referenced by any persisted URL and,
/// when explicitly confirmed, destroys them. The flow is read-only until cleanup is requested;
/// every deletion is re-validated against the fixed prefix and the current database references.
/// </summary>
public sealed class StoreImageMaintenanceService : IStoreImageMaintenanceService
{
    private const int MaxPageSize = 500;

    private static readonly Regex CloudinaryUrlRegex = new(
        @"https?://res\.cloudinary\.com/[^\s""'<>]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ApplicationDbContext _dbContext;
    private readonly ICloudinaryImageCatalog _catalog;
    private readonly CloudinaryMaintenanceOptions _options;
    private readonly ILogger<StoreImageMaintenanceService> _logger;

    public StoreImageMaintenanceService(
        ApplicationDbContext dbContext,
        ICloudinaryImageCatalog catalog,
        IOptions<CloudinaryMaintenanceOptions> options,
        ILogger<StoreImageMaintenanceService> logger)
    {
        _dbContext = dbContext;
        _catalog = catalog;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoreImageOrphanScanResultDto> ScanAsync(
        StoreImageOrphanScanRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scan = await BuildScanAsync(
            request.MaximumAssets,
            request.MinimumAgeMinutes,
            cancellationToken);

        return new StoreImageOrphanScanResultDto
        {
            Prefix = CloudinaryPublicId.StorePrefix,
            MinimumAgeMinutes = scan.MinimumAgeMinutes,
            ScannedCount = scan.ScannedCount,
            ReferencedCount = scan.ReferencedCount,
            RecentCount = scan.RecentCount,
            UnknownAgeCount = scan.UnknownAgeCount,
            OutsidePrefixCount = scan.OutsidePrefixCount,
            Orphans = scan.Candidates,
            Truncated = scan.Truncated
        };
    }

    public async Task<StoreImageOrphanCleanupResultDto> CleanupAsync(
        StoreImageOrphanCleanupRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var minimumAgeMinutes = ResolveMinimumAgeMinutes(request.MinimumAgeMinutes);
        if (!request.Confirm)
        {
            // No listing and no deletion: an unconfirmed call must not even touch Cloudinary.
            return new StoreImageOrphanCleanupResultDto
            {
                Prefix = CloudinaryPublicId.StorePrefix,
                MinimumAgeMinutes = minimumAgeMinutes,
                ConfirmationRequired = true
            };
        }

        var scan = await BuildScanAsync(
            request.MaximumAssets,
            request.MinimumAgeMinutes,
            cancellationToken);

        var maxDeletions = ResolveMaxDeletions(request.MaximumDeletions);
        var deletedPublicIds = new List<string>();
        var failures = new List<StoreImageOrphanDeletionFailureDto>();

        foreach (var candidate in scan.Candidates.Take(maxDeletions))
        {
            // The scan already validated prefix, references and age; repeat the prefix check here
            // so a future change to the candidate pipeline can never widen the destructive scope.
            if (!CloudinaryPublicId.IsUnderPrefix(candidate.PublicId, CloudinaryPublicId.StorePrefix))
            {
                continue;
            }

            try
            {
                var destroyed = await _catalog.DestroyAsync(candidate.PublicId, cancellationToken);
                if (destroyed)
                {
                    deletedPublicIds.Add(candidate.PublicId);
                    continue;
                }

                failures.Add(new StoreImageOrphanDeletionFailureDto
                {
                    PublicId = candidate.PublicId,
                    Error = "Cloudinary did not confirm the deletion."
                });
            }
            catch (Exception exception)
            {
                // One failure must not abort the batch; the asset is reported and remains available
                // for a later run.
                _logger.LogError(
                    exception,
                    "Cloudinary orphan deletion failed. PublicId={PublicId}",
                    candidate.PublicId);

                failures.Add(new StoreImageOrphanDeletionFailureDto
                {
                    PublicId = candidate.PublicId,
                    Error = SanitizeError(exception)
                });
            }
        }

        _logger.LogInformation(
            "Cloudinary orphan cleanup finished. Scanned={Scanned} Referenced={Referenced} Orphans={Orphans} Deleted={Deleted} Failed={Failed} Truncated={Truncated}",
            scan.ScannedCount,
            scan.ReferencedCount,
            scan.Candidates.Count,
            deletedPublicIds.Count,
            failures.Count,
            scan.Truncated);

        return new StoreImageOrphanCleanupResultDto
        {
            Prefix = CloudinaryPublicId.StorePrefix,
            MinimumAgeMinutes = scan.MinimumAgeMinutes,
            ScannedCount = scan.ScannedCount,
            OrphanCount = scan.Candidates.Count,
            DeletedCount = deletedPublicIds.Count,
            FailedCount = failures.Count,
            Truncated = scan.Truncated,
            DeletedPublicIds = deletedPublicIds,
            Failures = failures
        };
    }

    private async Task<ScanState> BuildScanAsync(
        int? requestedMaximumAssets,
        int? requestedMinimumAgeMinutes,
        CancellationToken cancellationToken)
    {
        var minimumAgeMinutes = ResolveMinimumAgeMinutes(requestedMinimumAgeMinutes);
        var maxAssets = ResolveMaxAssets(requestedMaximumAssets);
        var cutoffUtc = DateTime.UtcNow.AddMinutes(-minimumAgeMinutes);

        var referencedPublicIds = await LoadReferencedPublicIdsAsync(cancellationToken);

        var state = new ScanState(minimumAgeMinutes);
        string? cursor = null;
        var pages = 0;
        // The listing is bounded by assets, not pages, but a defensive page cap prevents a
        // provider cursor loop from spinning forever.
        var maxPages = Math.Max(1, (maxAssets / 100) + 2);

        while (state.ScannedCount < maxAssets && pages < maxPages)
        {
            pages++;
            var remaining = maxAssets - state.ScannedCount;
            var page = await _catalog.ListImagesAsync(
                CloudinaryPublicId.StorePrefix,
                Math.Min(remaining, MaxPageSize),
                cursor,
                cancellationToken);

            if (page.Resources.Count == 0)
            {
                state.Truncated = !string.IsNullOrWhiteSpace(page.NextCursor);
                break;
            }

            foreach (var resource in page.Resources)
            {
                if (state.ScannedCount >= maxAssets)
                {
                    state.Truncated = true;
                    break;
                }

                state.ScannedCount++;

                if (!CloudinaryPublicId.IsUnderPrefix(resource.PublicId, CloudinaryPublicId.StorePrefix))
                {
                    state.OutsidePrefixCount++;
                    continue;
                }

                if (referencedPublicIds.Contains(resource.PublicId))
                {
                    state.ReferencedCount++;
                    continue;
                }

                if (resource.CreatedAtUtc is null)
                {
                    // Without a trustworthy creation timestamp the asset cannot be proven old
                    // enough; protect it instead of guessing.
                    state.UnknownAgeCount++;
                    continue;
                }

                if (resource.CreatedAtUtc.Value > cutoffUtc)
                {
                    state.RecentCount++;
                    continue;
                }

                var ageMinutes = (int)Math.Max(0, (DateTime.UtcNow - resource.CreatedAtUtc.Value).TotalMinutes);
                state.Candidates.Add(new StoreImageOrphanCandidateDto
                {
                    PublicId = resource.PublicId,
                    CreatedAtUtc = DateTime.SpecifyKind(resource.CreatedAtUtc.Value, DateTimeKind.Utc),
                    AgeMinutes = ageMinutes,
                    IsPending = resource.PublicId.StartsWith(CloudinaryPublicId.PendingPrefix, StringComparison.OrdinalIgnoreCase)
                });
            }

            cursor = page.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        if (state.ScannedCount >= maxAssets && !string.IsNullOrWhiteSpace(cursor))
        {
            state.Truncated = true;
        }

        return state;
    }

    /// <summary>
    /// Builds the set of public ids still referenced by a persisted media URL. Product images and
    /// store logo/banner are the structured columns; landing page content is free text, so any
    /// Cloudinary URL embedded there is extracted defensively. A reference in any store protects
    /// the asset, even when the owning store no longer exists.
    /// </summary>
    private async Task<HashSet<string>> LoadReferencedPublicIdsAsync(CancellationToken cancellationToken)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var logoUrls = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.LogoUrl != null && x.LogoUrl != string.Empty)
            .Select(x => x.LogoUrl!)
            .ToListAsync(cancellationToken);
        AddRange(referenced, logoUrls);

        var bannerUrls = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.BannerUrl != null && x.BannerUrl != string.Empty)
            .Select(x => x.BannerUrl!)
            .ToListAsync(cancellationToken);
        AddRange(referenced, bannerUrls);

        var productUrls = await _dbContext.Products
            .AsNoTracking()
            .Where(x => x.ImageUrl != null && x.ImageUrl != string.Empty)
            .Select(x => x.ImageUrl!)
            .ToListAsync(cancellationToken);
        AddRange(referenced, productUrls);

        var landingContentValues = await _dbContext.LandingPageContents
            .AsNoTracking()
            .Select(x => x.Value)
            .ToListAsync(cancellationToken);

        foreach (var value in landingContentValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (Match match in CloudinaryUrlRegex.Matches(value))
            {
                AddReference(referenced, match.Value);
            }
        }

        return referenced;
    }

    private static void AddRange(HashSet<string> target, IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            AddReference(target, url);
        }
    }

    private static void AddReference(HashSet<string> target, string? url)
    {
        var publicId = CloudinaryPublicId.ExtractFromUrl(url);
        if (!string.IsNullOrWhiteSpace(publicId))
        {
            target.Add(publicId);
        }
    }

    private int ResolveMinimumAgeMinutes(int? requested) => requested is >= 0
        ? requested.Value
        : _options.EffectiveMinimumAssetAgeMinutes;

    private int ResolveMaxAssets(int? requested)
    {
        var configured = _options.EffectiveMaxAssetsPerScan;
        if (requested is > 0)
        {
            return Math.Min(requested.Value, configured);
        }

        return configured;
    }

    private int ResolveMaxDeletions(int? requested)
    {
        var configured = _options.EffectiveMaxDeletionsPerRun;
        if (requested is > 0)
        {
            return Math.Min(requested.Value, configured);
        }

        return configured;
    }

    private static string SanitizeError(Exception exception)
    {
        // Never copy provider payloads, URLs or credentials into the response. The exception type
        // is enough to correlate with the structured server-side log.
        return $"{exception.GetType().Name}: Cloudinary deletion failed.";
    }

    private sealed class ScanState
    {
        public ScanState(int minimumAgeMinutes)
        {
            MinimumAgeMinutes = minimumAgeMinutes;
        }

        public int MinimumAgeMinutes { get; }

        public int ScannedCount { get; set; }

        public int ReferencedCount { get; set; }

        public int RecentCount { get; set; }

        public int UnknownAgeCount { get; set; }

        public int OutsidePrefixCount { get; set; }

        public bool Truncated { get; set; }

        public List<StoreImageOrphanCandidateDto> Candidates { get; } = new();
    }
}
