namespace Urbeat.Infrastructure.Services.Payments;

public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    public string BaseUrl { get; init; } = "https://api.mercadopago.com";

    public string AccessToken { get; init; } = string.Empty;

    public string? NotificationUrl { get; init; }

    public string? SuccessUrl { get; init; }

    public string? FailureUrl { get; init; }

    public string? PendingUrl { get; init; }
}
