using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Internal transport for outbound WhatsApp messages. Deliberately not exposed through a public
/// endpoint: business flows decide who receives what, this abstraction only delivers the message.
/// </summary>
public interface IWhatsAppMessageSender
{
    Task<WhatsAppSendMessageResult> SendTextMessageAsync(
        WhatsAppTextMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an image message using a public HTTPS link. This is intentionally a transport
    /// primitive: callers remain responsible for resolving which store (and therefore which
    /// logo) should be used for a given recipient.
    /// </summary>
    Task<WhatsAppSendMessageResult> SendImageMessageAsync(
        WhatsAppImageMessageRequest request,
        CancellationToken cancellationToken = default);
}
