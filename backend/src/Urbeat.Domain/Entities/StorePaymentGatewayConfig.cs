namespace Urbeat.Domain.Entities;

public sealed class StorePaymentGatewayConfig : BaseEntity
{
    public Guid StoreId { get; set; }

    public PaymentGateway Gateway { get; set; }

    public string EncryptedAccessToken { get; set; } = string.Empty;

    public string? EncryptedNotificationUrl { get; set; }

    public string Environment { get; set; } = "Sandbox";

    public bool IsActive { get; set; }
}
