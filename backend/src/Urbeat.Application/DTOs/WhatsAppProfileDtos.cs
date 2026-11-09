namespace Urbeat.Application.DTOs;

/// <summary>
/// Outcome of applying the institutional logo to the WhatsApp business profile. The provider
/// upload handle and the access token are never stored or returned.
/// </summary>
public sealed class WhatsAppProfilePictureUpdateResult
{
    public bool Success { get; init; }

    /// <summary>Short local/provider code suitable for logs; never contains secrets.</summary>
    public string? ErrorCode { get; init; }

    public static WhatsAppProfilePictureUpdateResult Updated() => new()
    {
        Success = true
    };

    public static WhatsAppProfilePictureUpdateResult Failed(string errorCode) => new()
    {
        Success = false,
        ErrorCode = errorCode
    };
}
