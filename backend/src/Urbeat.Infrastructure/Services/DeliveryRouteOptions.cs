namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Configuration of the temporary public delivery route link. The expiration is short by
/// design (a shift, not a permanent record) and the path is served by the same frontend origin
/// configured in <see cref="Email.EmailConfirmationOptions.FrontendBaseUrl"/>.
/// </summary>
public sealed class DeliveryRouteOptions
{
    public const string SectionName = "DeliveryRoute";

    public int ExpirationHours { get; set; } = 12;

    public string PublicPath { get; set; } = "/rota";

    /// <summary>Frontend path of the individual delivery link (no route/batch).</summary>
    public string IndividualPublicPath { get; set; } = "/entrega";

    public int MinExpirationHours { get; set; } = 1;

    public int MaxExpirationHours { get; set; } = 48;
}
