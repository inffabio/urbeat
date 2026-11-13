using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services;

public sealed class CloudinaryImageUploadService : IImageUploadService
{
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryImageUploadService> _logger;

    public CloudinaryImageUploadService(
        IOptions<CloudinaryOptions> options,
        ILogger<CloudinaryImageUploadService> logger)
    {
        _logger = logger;

        var account = new Account(
            options.Value.CloudName,
            options.Value.ApiKey,
            options.Value.ApiSecret);

        _cloudinary = new Cloudinary(account);
        _cloudinary.Api.Secure = true;
    }

    public async Task<string> UploadAsync(Stream fileStream, string fileName, string folder, CancellationToken cancellationToken = default)
    {
        var uploadParams = BuildUploadParams(fileStream, fileName, folder);

        var uploadResult = await _cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (uploadResult.Error != null)
        {
            _logger.LogError("Cloudinary upload error: {Error}", uploadResult.Error.Message);
            throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
        }

        return uploadResult.SecureUrl.ToString();
    }

    /// <summary>
    /// Builds the upload parameters without contacting Cloudinary so the configuration can be
    /// asserted in tests. Kept separate from <see cref="UploadAsync"/> so unit tests never need
    /// real credentials or network access.
    /// </summary>
    internal static ImageUploadParams BuildUploadParams(Stream fileStream, string fileName, string folder)
    {
        return new ImageUploadParams
        {
            File = new FileDescription(fileName, fileStream),
            Folder = folder,
            UseFilename = true,
            UniqueFilename = true,
            Overwrite = false,
            Transformation = BuildIncomingTransformation()
        };
    }

    /// <summary>
    /// Builds the incoming transformation applied to uploaded assets before they are stored.
    /// It only normalizes resolution and quality, which is the supported use case for incoming
    /// transformations. An isolated <c>q_auto</c> is allowed here because quality normalization
    /// can be resolved at upload time. Delivery-only parameters <c>f_auto</c> and <c>dpr_auto</c>
    /// must NOT be used: format and device-pixel-ratio selection depend on the requesting browser,
    /// which does not exist during upload. Baking them into the stored asset produces an asset
    /// whose format/DPR is not resolved, so it renders black in the browser.
    /// </summary>
    internal static Transformation BuildIncomingTransformation()
    {
        return new Transformation()
            .Width(1920)
            .Height(1920)
            .Crop("limit")
            .Quality("auto")
            .Flags("progressive");
    }

    public async Task DeleteAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return;

        // Cloudinary destroys by PublicId. Persisted URLs and maintenance scans must derive it
        // with the same rule, otherwise a referenced asset can be mistaken for an orphan.
        var publicId = CloudinaryPublicId.ExtractFromUrl(imageUrl);
        if (string.IsNullOrEmpty(publicId))
        {
            _logger.LogWarning("Could not extract PublicId from URL: {ImageUrl}", imageUrl);
            throw new InvalidOperationException("Could not extract Cloudinary public id from the image URL.");
        }

        var deletionParams = new DeletionParams(publicId)
        {
            ResourceType = ResourceType.Image
        };

        var deletionResult = await _cloudinary.DestroyAsync(deletionParams);

        // "not found" is idempotent success: the desired end state (no asset) already holds.
        if (deletionResult.Result is "ok" or "not found")
        {
            return;
        }

        _logger.LogWarning("Failed to delete image from Cloudinary. PublicId: {PublicId}. Result: {Result}", publicId, deletionResult.Result);
        throw new InvalidOperationException($"Cloudinary deletion failed for public id '{publicId}': {deletionResult.Result}");
    }
}
