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

    /// <summary>
    /// When true, the adapter may synthesize fake checkout/payment responses if no access token is
    /// configured. Defaults to false so production never falls back to a simulated approval; enable
    /// it only in Development and tests.
    /// </summary>
    public bool AllowSimulation { get; init; }
}
