using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Internal operation that applies the institutional urbeat logo to the WhatsApp business
/// profile. The profile picture belongs to the Phone Number ID (it is global, not per store) and
/// the operation is idempotent: repeating it re-uploads and re-applies the same image.
/// It is never executed from startup or from a webhook request; an authenticated operator must
/// invoke it explicitly.
/// </summary>
public interface IWhatsAppBusinessProfileService
{
    Task<WhatsAppProfilePictureUpdateResult> UpdateProfilePictureAsync(
        CancellationToken cancellationToken = default);
}
