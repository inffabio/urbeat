namespace Urbeat.Domain.Entities;

public sealed class PaymentStatusHistory : BaseEntity
{
    public Guid PaymentId { get; set; }

    public PaymentStatus? PreviousStatus { get; set; }

    public PaymentStatus NewStatus { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string? RawPayload { get; set; }
}
