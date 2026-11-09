namespace Urbeat.Application.DTOs;

/// <summary>
/// Outcome of ingesting a WhatsApp Cloud API webhook envelope. A signed envelope is always
/// acknowledged to Meta; <see cref="DuplicateEvents"/> only reports events already recorded.
/// </summary>
public sealed class ProcessWhatsAppWebhookResultDto
{
    public bool Accepted { get; init; }

    public bool InvalidPayload { get; init; }

    public int RecordedEvents { get; init; }

    public int DuplicateEvents { get; init; }

    /// <summary>
    /// Events ignored because their <c>metadata.phone_number_id</c> does not match the configured
    /// <c>WhatsApp:PhoneNumberId</c>. They are acknowledged (not persisted) so Meta does not retry.
    /// </summary>
    public int IgnoredEvents { get; init; }
}

/// <summary>
/// A text message to send through the WhatsApp Cloud API. <see cref="To"/> must be an E.164
/// number; the sender normalizes it to the digits-only form required by Meta.
/// </summary>
public sealed record WhatsAppTextMessageRequest(string To, string Body, bool PreviewUrl = false);

/// <summary>
/// An image message to send through the WhatsApp Cloud API. <see cref="To"/> must be an E.164
/// number and <see cref="ImageUrl"/> must be a public absolute HTTPS URL reachable by Meta, such
/// as a store logo. The URL is never logged.
/// </summary>
public sealed record WhatsAppImageMessageRequest(string To, string ImageUrl, string? Caption = null);

/// <summary>
/// Result of a Cloud API send. It intentionally never carries the access token or any request
/// header value; <see cref="ErrorCode"/> is a short provider/local code suitable for logs.
/// </summary>
public sealed class WhatsAppSendMessageResult
{
    public bool Success { get; init; }

    public string? MessageId { get; init; }

    public string? ErrorCode { get; init; }

    public static WhatsAppSendMessageResult Sent(string? messageId) => new()
    {
        Success = true,
        MessageId = messageId
    };

    public static WhatsAppSendMessageResult Failed(string errorCode) => new()
    {
        Success = false,
        ErrorCode = errorCode
    };
}
