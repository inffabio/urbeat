using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory replacement for the Cloudinary catalog. Records every destroy call and serves the
/// prefix-paginated listing contract without any network access.
/// </summary>
public sealed class FakeCloudinaryImageCatalog : ICloudinaryImageCatalog
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
            throw new InvalidOperationException("Fake Cloudinary deletion failure.");
        }

        DestroyedPublicIds.Add(publicId);
        return Task.FromResult(true);
    }
}
