using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Cloudinary-backed implementation of the administrative asset catalog. Only image resources of
/// type <c>upload</c> are listed, and deletion is always by public id.
/// </summary>
public sealed class CloudinaryImageCatalog : ICloudinaryImageCatalog
{
    private const int ProviderMaxResults = 500;

    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryImageCatalog> _logger;

    public CloudinaryImageCatalog(
        IOptions<CloudinaryOptions> options,
        ILogger<CloudinaryImageCatalog> logger)
    {
        _logger = logger;

        var account = new Account(
            options.Value.CloudName,
            options.Value.ApiKey,
            options.Value.ApiSecret);

        _cloudinary = new Cloudinary(account);
        _cloudinary.Api.Secure = true;
    }

    public async Task<CloudinaryImagePageDto> ListImagesAsync(
        string prefix,
        int maxResults,
        string? nextCursor,
        CancellationToken cancellationToken = default)
    {
        var parameters = new ListResourcesByPrefixParams
        {
            Prefix = prefix,
            ResourceType = ResourceType.Image,
            Type = "upload",
            MaxResults = Math.Clamp(maxResults, 1, ProviderMaxResults),
            NextCursor = nextCursor
        };

        var result = await _cloudinary.ListResourcesAsync(parameters, cancellationToken);
        if (result.Error is not null)
        {
            // Provider messages are logged but never surfaced: they may describe account state.
            _logger.LogError("Cloudinary list error for prefix {Prefix}: {Error}", prefix, result.Error.Message);
            throw new InvalidOperationException("Cloudinary resource listing failed.");
        }

        var resources = result.Resources?
            .Where(resource => !string.IsNullOrWhiteSpace(resource.PublicId))
            .Select(resource => new CloudinaryImageResourceDto(
                resource.PublicId,
                ParseCreatedAt(resource.CreatedAt),
                resource.Format))
            .ToList() ?? new List<CloudinaryImageResourceDto>();

        return new CloudinaryImagePageDto
        {
            Resources = resources,
            NextCursor = string.IsNullOrWhiteSpace(result.NextCursor) ? null : result.NextCursor
        };
    }

    public async Task<bool> DestroyAsync(string publicId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CloudinaryPublicId.IsUnderPrefix(publicId, CloudinaryPublicId.StorePrefix))
        {
            // Defense in depth: the catalog refuses to touch anything outside the store prefix even
            // if a caller passes an arbitrary public id.
            _logger.LogWarning("Rejected Cloudinary destroy for public id outside the store prefix: {PublicId}", publicId);
            return false;
        }

        var deletionParams = new DeletionParams(publicId)
        {
            ResourceType = ResourceType.Image
        };

        var deletionResult = await _cloudinary.DestroyAsync(deletionParams);
        if (deletionResult.Result is "ok" or "not found")
        {
            return true;
        }

        _logger.LogWarning(
            "Failed to delete image from Cloudinary. PublicId: {PublicId}. Result: {Result}",
            publicId,
            deletionResult.Result);

        return false;
    }

    private static DateTime? ParseCreatedAt(string? createdAt)
    {
        if (string.IsNullOrWhiteSpace(createdAt))
        {
            return null;
        }

        return DateTime.TryParse(
            createdAt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }
}
