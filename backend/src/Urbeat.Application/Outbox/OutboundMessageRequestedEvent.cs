namespace Urbeat.Application.Outbox;

public sealed class OutboundMessageRequestedEvent
{
    public string Channel { get; set; } = string.Empty;

    public string DeliveryKey { get; set; } = string.Empty;

    public string Recipient { get; set; } = string.Empty;

    public string ToName { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Identifies which template to render. When set, <see cref="Body"/> and <see cref="Subject"/>
    /// are intentionally left empty and the handler renders the content at send time from a safe
    /// reference (<see cref="CorrelationId"/>), so no short code or token is ever persisted here.
    /// </summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>
    /// Safe reference the handler resolves at send time to obtain the confirmation short code and
    /// token from the token cache. Never carries the token itself.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>Recipient user reference used by template-based sends to resolve the address.</summary>
    public Guid? UserId { get; set; }
}
