using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Administrative view over Cloudinary image assets: prefix listing and deletion by public id.
/// Kept separate from <see cref="IImageUploadService"/> so the upload contract stays minimal and
/// maintenance code cannot upload.
/// </summary>
public interface ICloudinaryImageCatalog
{
    Task<CloudinaryImagePageDto> ListImagesAsync(
        string prefix,
        int maxResults,
        string? nextCursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Destroys one image by public id. Returns true when Cloudinary reports success or that the
    /// asset is already absent; returns false for any other provider result.
    /// </summary>
    Task<bool> DestroyAsync(string publicId, CancellationToken cancellationToken = default);
}
