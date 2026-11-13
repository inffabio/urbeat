namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Single normalization rule for Cloudinary URLs. Both the upload service and the orphan
/// maintenance flow must derive public ids the same way; otherwise a referenced asset could be
/// misclassified as an orphan and destroyed.
/// </summary>
internal static class CloudinaryPublicId
{
    public const string StorePrefix = "stores/";
    public const string PendingPrefix = "stores/_pending/";

    /// <summary>
    /// Extracts the public id from a Cloudinary delivery URL, dropping the optional version
    /// segment and the file extension while preserving subfolders.
    /// Example: <c>https://res.cloudinary.com/cloud/image/upload/v1712345678/stores/loja/logo/file.jpg</c>
    /// becomes <c>stores/loja/logo/file</c>.
    /// </summary>
    public static string ExtractFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        // Only Cloudinary delivery hosts are accepted. Without this, any URL whose path happens
        // to contain "/upload/" could be normalized into a destructive public id.
        if (!IsCloudinaryHost(uri.Host))
        {
            return string.Empty;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var uploadIndex = Array.FindIndex(segments, segment =>
            segment.Equals("upload", StringComparison.OrdinalIgnoreCase));

        if (uploadIndex < 0 || uploadIndex + 1 >= segments.Length)
        {
            return string.Empty;
        }

        // Delivery URLs may carry transformation segments between "upload" and the version,
        // e.g. /upload/c_limit,w_1920/v1712345678/stores/... The version segment is the reliable
        // boundary; without one the public id starts right after "upload".
        var versionIndex = Array.FindIndex(
            segments,
            uploadIndex + 1,
            segment => IsVersionSegment(segment));

        var startIndex = versionIndex >= 0 ? versionIndex + 1 : uploadIndex + 1;
        if (startIndex >= segments.Length)
        {
            return string.Empty;
        }

        var publicId = string.Join('/', segments.Skip(startIndex));
        var extensionIndex = publicId.LastIndexOf('.');
        if (extensionIndex > 0 && publicId.IndexOf('/', extensionIndex) < 0)
        {
            publicId = publicId[..extensionIndex];
        }

        return Uri.UnescapeDataString(publicId).Trim('/');
    }

    /// <summary>
    /// True when the public id belongs to <paramref name="prefix"/> and has a non-empty remainder.
    /// A prefix of <c>stores/</c> rejects <c>stores</c> itself, <c>stores-old/x</c> and
    /// <c>other/stores/x</c>.
    /// </summary>
    public static bool IsUnderPrefix(string? publicId, string prefix)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return false;
        }

        var normalizedPrefix = NormalizePrefix(prefix);
        var normalizedPublicId = publicId.Trim().TrimStart('/');

        return normalizedPublicId.Length > normalizedPrefix.Length
            && normalizedPublicId.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizePrefix(string prefix)
    {
        var normalized = (prefix ?? string.Empty).Trim().TrimStart('/');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    private static bool IsCloudinaryHost(string host)
    {
        return host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("cloudinary.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".cloudinary.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionSegment(string segment)
    {
        if (segment.Length < 2 || segment[0] is not ('v' or 'V'))
        {
            return false;
        }

        for (var index = 1; index < segment.Length; index++)
        {
            if (!char.IsAsciiDigit(segment[index]))
            {
                return false;
            }
        }

        return true;
    }
}
