using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Reusable composition for store-branded outbound messages. It is internal infrastructure and is
/// not wired to any business flow yet: the current webhook neither resolves a store nor replies
/// automatically. A future flow that has already resolved the store serving a conversation can
/// pass the store logo and the recipient here; the sender only delivers the image.
/// </summary>
public interface IWhatsAppStoreMessageComposer
{
    Task<WhatsAppSendMessageResult> SendStoreLogoMessageAsync(
        string storeLogoUrl,
        string recipient,
        string? caption = null,
        CancellationToken cancellationToken = default);
}
