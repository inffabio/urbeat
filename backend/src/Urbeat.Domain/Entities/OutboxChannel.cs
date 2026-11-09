namespace Urbeat.Domain.Entities;

/// <summary>
/// Processing lane for an outbox handler delivery. Each channel is consumed by a dedicated host so
/// an external communication failure (e-mail/WhatsApp) cannot stall business handlers (SignalR,
/// printing) and vice versa.
/// </summary>
public enum OutboxChannel
{
    Business = 0,
    Communications = 1
}
