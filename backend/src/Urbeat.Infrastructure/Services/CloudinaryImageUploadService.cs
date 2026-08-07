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
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, fileStream),
            Folder = folder,
            UseFilename = true,
            UniqueFilename = true,
            Overwrite = false,
            // Automatic image optimization:
            // - Limit dimensions to 1920x1920 (downscales large images, preserves small ones)
            // - q_auto for optimal compression without visible loss
            // - f_auto for WebP/AVIF delivery when browser supports
            // - dpr_auto for responsive device pixel ratio
            // - fl_progressive for progressive JPEG loading
            Transformation = new CloudinaryDotNet.Transformation()
                .Width(1920)
                .Height(1920)
                .Crop("limit")
                .Quality("auto")
                .FetchFormat("auto")
                .Dpr("auto")
                .Flags("progressive")
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (uploadResult.Error != null)
        {
            _logger.LogError("Cloudinary upload error: {Error}", uploadResult.Error.Message);
            throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
        }

        return uploadResult.SecureUrl.ToString();
    }

    public async Task DeleteAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return;

        // Cloudinary needs the PublicId to delete. 
        // We can extract it from the URL: https://res.cloudinary.com/<cloud>/image/upload/v1234/<folder>/<public_id>.ext
        var publicId = ExtractPublicIdFromUrl(imageUrl);
        if (string.IsNullOrEmpty(publicId))
        {
            _logger.LogWarning("Could not extract PublicId from URL: {ImageUrl}", imageUrl);
            return;
        }

        var deletionParams = new DeletionParams(publicId)
        {
            ResourceType = ResourceType.Image
        };

        var deletionResult = await _cloudinary.DestroyAsync(deletionParams);
        if (deletionResult.Result != "ok")
        {
            _logger.LogWarning("Failed to delete image from Cloudinary. PublicId: {PublicId}. Result: {Result}", publicId, deletionResult.Result);
        }
    }

    private static string ExtractPublicIdFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            
            // Expected path parts typically: 
            // /, image/, upload/, v12356/, folder/, public_id.ext
            // We need to capture from folder onwards, removing the extension
            
            var uploadIndex = Array.FindIndex(segments, s => s.Equals("upload/", StringComparison.OrdinalIgnoreCase));
            if (uploadIndex == -1 || uploadIndex + 2 >= segments.Length) 
                return string.Empty;

            // Skip "v123.../" 
            var startIndex = uploadIndex + 2;
            
            var publicIdWithExtension = string.Join("", segments.Skip(startIndex));
            var extensionIndex = publicIdWithExtension.LastIndexOf('.');
            if (extensionIndex > 0)
            {
                return publicIdWithExtension.Substring(0, extensionIndex);
            }

            return publicIdWithExtension;
        }
        catch
        {
            return string.Empty;
        }
    }
}
