using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;

namespace Urbeat.Application.WhatsApp;

public sealed class WhatsAppStoreMessageComposer : IWhatsAppStoreMessageComposer
{
    private readonly IWhatsAppMessageSender _messageSender;

    public WhatsAppStoreMessageComposer(IWhatsAppMessageSender messageSender)
    {
        _messageSender = messageSender;
    }

    public Task<WhatsAppSendMessageResult> SendStoreLogoMessageAsync(
        string storeLogoUrl,
        string recipient,
        string? caption = null,
        CancellationToken cancellationToken = default)
    {
        return _messageSender.SendImageMessageAsync(
            new WhatsAppImageMessageRequest(recipient, storeLogoUrl, caption),
            cancellationToken);
    }
}
