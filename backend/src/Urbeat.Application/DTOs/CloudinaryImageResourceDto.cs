namespace Urbeat.Application.DTOs;

/// <summary>
/// Cloudinary image resource projected for maintenance flows. <see cref="PublicId"/> is the only
/// stable identifier; delivery URLs are intentionally not exposed by this contract.
/// </summary>
public sealed record CloudinaryImageResourceDto(string PublicId, DateTime? CreatedAtUtc, string? Format);

/// <summary>
/// One page of Cloudinary image resources returned by a prefix listing call.
/// </summary>
public sealed class CloudinaryImagePageDto
{
    public IReadOnlyList<CloudinaryImageResourceDto> Resources { get; init; } = Array.Empty<CloudinaryImageResourceDto>();

    public string? NextCursor { get; init; }
}
