namespace Urbeat.Domain.Entities;

public sealed class PaymentGatewayTransactionLog : BaseEntity
{
    public Guid? PaymentId { get; set; }

    public Guid OrderId { get; set; }

    public Guid StoreId { get; set; }

    public PaymentGateway Gateway { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? RequestPayload { get; set; }

    public string? ResponsePayload { get; set; }

    public int? StatusCode { get; set; }

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public string? CorrelationId { get; set; }
}
