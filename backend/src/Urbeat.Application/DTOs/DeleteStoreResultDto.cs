namespace Urbeat.Application.DTOs;

/// <summary>
/// Outcome of a full store deletion request. The store row is already committed when
/// <see cref="Deleted"/> is true; Cloudinary cleanup is best-effort and only reported here.
/// </summary>
public sealed class DeleteStoreResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool Deleted { get; init; }

    /// <summary>Number of Cloudinary URLs collected before the database mutation.</summary>
    public int AssetsAttempted { get; init; }

    /// <summary>Number of Cloudinary deletions that completed without throwing.</summary>
    public int AssetsDeleted { get; init; }

    /// <summary>Number of Cloudinary deletions that failed. The database state is unaffected.</summary>
    public int AssetsFailed { get; init; }
}
