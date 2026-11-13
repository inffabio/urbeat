namespace Urbeat.Application.DTOs;

public sealed class StoreImageOrphanScanRequestDto
{
    /// <summary>Upper bound of Cloudinary resources inspected. Clamped to the configured maximum.</summary>
    public int? MaximumAssets { get; init; }

    /// <summary>
    /// Minimum age, in minutes, for a resource to be considered deletable. Uploads younger than
    /// this window are protected regardless of folder so a form that is still being filled cannot
    /// lose its image. <c>_pending</c> assets always obey this window.
    /// </summary>
    public int? MinimumAgeMinutes { get; init; }
}

public sealed class StoreImageOrphanCandidateDto
{
    public string PublicId { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public int AgeMinutes { get; init; }

    /// <summary>True for assets under <c>stores/_pending/</c> (uploads made before the store existed).</summary>
    public bool IsPending { get; init; }
}

public sealed class StoreImageOrphanScanResultDto
{
    public string Prefix { get; init; } = string.Empty;

    public int MinimumAgeMinutes { get; init; }

    public int ScannedCount { get; init; }

    /// <summary>Assets still referenced by a persisted URL column; never deletable.</summary>
    public int ReferencedCount { get; init; }

    /// <summary>Assets younger than the minimum age; protected.</summary>
    public int RecentCount { get; init; }

    /// <summary>Assets without a parseable Cloudinary creation timestamp; protected.</summary>
    public int UnknownAgeCount { get; init; }

    /// <summary>Assets returned by the listing but outside the fixed prefix; ignored.</summary>
    public int OutsidePrefixCount { get; init; }

    public IReadOnlyList<StoreImageOrphanCandidateDto> Orphans { get; init; } = Array.Empty<StoreImageOrphanCandidateDto>();

    /// <summary>True when the scan stopped at the asset bound before exhausting the listing.</summary>
    public bool Truncated { get; init; }
}

public sealed class StoreImageOrphanCleanupRequestDto
{
    /// <summary>Explicit confirmation. Without it nothing is deleted.</summary>
    public bool Confirm { get; init; }

    public int? MaximumAssets { get; init; }

    public int? MinimumAgeMinutes { get; init; }

    /// <summary>Maximum deletions for this call. Clamped to the configured maximum.</summary>
    public int? MaximumDeletions { get; init; }
}

public sealed class StoreImageOrphanDeletionFailureDto
{
    public string PublicId { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;
}

public sealed class StoreImageOrphanCleanupResultDto
{
    public string Prefix { get; init; } = string.Empty;

    public int MinimumAgeMinutes { get; init; }

    /// <summary>True when the request was not confirmed; no deletion was attempted.</summary>
    public bool ConfirmationRequired { get; init; }

    public int ScannedCount { get; init; }

    public int OrphanCount { get; init; }

    public int DeletedCount { get; init; }

    public int FailedCount { get; init; }

    public bool Truncated { get; init; }

    public IReadOnlyList<string> DeletedPublicIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<StoreImageOrphanDeletionFailureDto> Failures { get; init; } = Array.Empty<StoreImageOrphanDeletionFailureDto>();
}
