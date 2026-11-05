using Urbeat.Domain.Entities;

namespace Urbeat.Application.SystemParameters;

/// <summary>
/// How the admin UI should render a system parameter value. The wire name is
/// lowercase so the Angular client can switch on it without a lookup table.
/// </summary>
public enum SystemParameterDisplayKind
{
    Text,
    Multiline,
    Integer,
    Decimal,
    Currency,
    Percentage,
    Url,
    Email,
    Cron,
    Boolean,
    Json,
    Secret
}

public static class SystemParameterDisplayKindExtensions
{
    public static string ToWireName(this SystemParameterDisplayKind kind) => kind switch
    {
        SystemParameterDisplayKind.Text => "text",
        SystemParameterDisplayKind.Multiline => "multiline",
        SystemParameterDisplayKind.Integer => "integer",
        SystemParameterDisplayKind.Decimal => "decimal",
        SystemParameterDisplayKind.Currency => "currency",
        SystemParameterDisplayKind.Percentage => "percentage",
        SystemParameterDisplayKind.Url => "url",
        SystemParameterDisplayKind.Email => "email",
        SystemParameterDisplayKind.Cron => "cron",
        SystemParameterDisplayKind.Boolean => "boolean",
        SystemParameterDisplayKind.Json => "json",
        SystemParameterDisplayKind.Secret => "secret",
        _ => "text"
    };
}

/// <summary>
/// Authoritative validation/rendering policy for one parameter key.
/// </summary>
public sealed record SystemParameterPolicy(
    SystemParameterDisplayKind Display,
    int MaxLength,
    decimal? MinValue = null,
    decimal? MaxValue = null,
    string? Unit = null,
    bool Restricted = false,
    bool ReadOnly = false,
    bool Secret = false,
    string? Pattern = null,
    string? PatternMessage = null);

/// <summary>
/// Metadata policy per system parameter key. The database stores
/// <c>SystemParameter.Value</c> as an unbounded text column (no explicit
/// max length in <c>ApplicationDbContext</c>), so limits here are enforced by
/// the API before persisting and never truncate existing values.
/// </summary>
public static class SystemParameterCatalog
{
    public const string SecretMask = "********";
    public const int DefaultValueMaxLength = 2000;
    public const int DescriptionMaxLength = 500;
    public const int GroupMaxLength = 100;
    public const int KeyMaxLength = 200;

    private static readonly Dictionary<string, SystemParameterPolicy> Policies = BuildPolicies();

    public static SystemParameterPolicy Resolve(string key, SystemParameterType type, string? group)
    {
        var policy = Policies.TryGetValue(key, out var known)
            ? known
            : Fallback(type, group);

        return ApplySecretRules(key, policy);
    }

    public static bool IsSecretKey(string key)
    {
        var segment = key.Split('.').LastOrDefault()?.Trim().ToLowerInvariant() ?? string.Empty;
        return segment is "secret" or "clientsecret" or "apikey" or "api_key" or "privatekey"
            or "password" or "credential" or "credentials"
            || segment.Contains("secret", StringComparison.Ordinal);
    }

    private static SystemParameterPolicy Fallback(SystemParameterType type, string? group)
    {
        var restricted = string.Equals(group, "Security", StringComparison.OrdinalIgnoreCase)
            || string.Equals(group, "Url", StringComparison.OrdinalIgnoreCase);

        return type switch
        {
            SystemParameterType.Boolean => new(SystemParameterDisplayKind.Boolean, 10, Restricted: restricted),
            SystemParameterType.Int32 => new(SystemParameterDisplayKind.Integer, 11, Restricted: restricted),
            SystemParameterType.Decimal => new(SystemParameterDisplayKind.Decimal, 18, Restricted: restricted),
            SystemParameterType.Json => new(SystemParameterDisplayKind.Json, DefaultValueMaxLength, Restricted: restricted),
            _ => new(SystemParameterDisplayKind.Text, DefaultValueMaxLength, Restricted: restricted)
        };
    }

    private static SystemParameterPolicy ApplySecretRules(string key, SystemParameterPolicy policy)
    {
        if (!IsSecretKey(key))
        {
            return policy;
        }

        // Secrets are never returned in full. Rotating a secret must remain a
        // deliberate, out-of-band operation until a dedicated secret workflow exists.
        return policy with
        {
            Display = SystemParameterDisplayKind.Secret,
            ReadOnly = true,
            Secret = true,
            Restricted = true
        };
    }

    private static Dictionary<string, SystemParameterPolicy> BuildPolicies()
    {
        var policies = new Dictionary<string, SystemParameterPolicy>(StringComparer.Ordinal);

        void Add(string key, SystemParameterPolicy policy) => policies[key] = policy;

        void AddBoolean(string key, bool restricted = false) =>
            Add(key, new(SystemParameterDisplayKind.Boolean, 10, Restricted: restricted));

        void AddInt(string key, int min, int max, string? unit = null, bool restricted = false) =>
            Add(key, new(SystemParameterDisplayKind.Integer, 11, min, max, unit, restricted));

        void AddText(string key, int maxLength, bool restricted = false) =>
            Add(key, new(SystemParameterDisplayKind.Text, maxLength, Restricted: restricted));

        void AddMultiline(string key, int maxLength) =>
            Add(key, new(SystemParameterDisplayKind.Multiline, maxLength));

        void AddUrl(string key, bool restricted = true) =>
            Add(key, new(SystemParameterDisplayKind.Url, 500, Restricted: restricted));

        void AddEmail(string key) =>
            Add(key, new(SystemParameterDisplayKind.Email, 320));

        void AddDecimal(string key, SystemParameterDisplayKind display, decimal min, decimal max) =>
            Add(key, new(display, 18, min, max));

        // ── Security ────────────────────────────────────────────────
        AddBoolean("Password.RequireDigit", restricted: true);
        AddBoolean("Password.RequireLowercase", restricted: true);
        AddBoolean("Password.RequireUppercase", restricted: true);
        AddBoolean("Password.RequireNonAlphanumeric", restricted: true);
        AddInt("Password.RequiredLength", 6, 128, "caracteres", restricted: true);
        AddInt("Lockout.MaxFailedAccessAttempts", 1, 100, "tentativas", restricted: true);
        AddInt("Lockout.DefaultLockoutMinutes", 1, 1440, "min", restricted: true);
        AddInt("Jwt.ExpirationMinutes", 1, 1440, "min", restricted: true);
        AddInt("Jwt.RefreshTokenDays", 1, 365, "dias", restricted: true);
        AddInt("Jwt.ClockSkewMinutes", 0, 60, "min", restricted: true);

        // ── Business ────────────────────────────────────────────────
        AddInt("Customer.MaxAddresses", 1, 50, "endereços");
        AddInt("Order.DefaultPageSize", 1, 1000, "itens");
        AddInt("Order.MaxPageSize", 1, 1000, "itens");
        AddText("Order.CodePrefix", 20);
        AddInt("Order.CodeLength", 1, 64, "caracteres");
        AddText("Order.CodeChars", 200);
        AddInt("Order.CodeMaxAttempts", 1, 100, "tentativas");
        AddInt("Notification.MaxReturned", 1, 1000, "itens");
        AddDecimal("Store.DefaultDeliveryFee", SystemParameterDisplayKind.Currency, 0m, 1_000_000m);
        AddDecimal("Store.DefaultMinimumOrderValue", SystemParameterDisplayKind.Currency, 0m, 1_000_000m);
        AddInt("Subscription.DueSoonDays", 0, 365, "dias");
        AddInt("Subscription.DefaultBillingCycleDays", 1, 365, "dias");
        AddText("Subscription.BillingType", 30);
        AddText("Subscription.Cycle", 20);
        AddText("Subscription.Description", 200);

        // ── URLs / Endpoints ────────────────────────────────────────
        AddUrl("Url.FrontendBaseUrl");
        AddText("Url.EmailConfirmPath", 200, restricted: true);
        AddUrl("Url.ViaCep");
        AddText("Url.ViaCepPath", 200, restricted: true);
        AddUrl("Url.MercadoPago");
        AddText("Url.MercadoPagoCheckoutPath", 200, restricted: true);
        AddText("Url.MercadoPagoPaymentPath", 200, restricted: true);
        AddUrl("Url.Asaas");
        AddText("Url.AsaasCustomersPath", 200, restricted: true);
        AddText("Url.AsaasSubscriptionsPath", 200, restricted: true);
        AddText("Url.HangfireDashboard", 200, restricted: true);
        AddText("Url.SellerHub", 200, restricted: true);
        AddText("Url.CustomerHub", 200, restricted: true);

        // ── Email ───────────────────────────────────────────────────
        AddEmail("Email.FromAddress");
        AddText("Email.FromName", 120);
        AddInt("Email.SmtpPort", 1, 65535, "porta", restricted: true);
        AddBoolean("Email.UseStartTls", restricted: true);
        AddBoolean("Email.LogOnly", restricted: true);
        AddMultiline("Email.ConfirmationCustomerSubject", 200);
        AddMultiline("Email.ConfirmationCustomerTitle", 200);
        AddMultiline("Email.ConfirmationCustomerGreeting", 200);
        AddMultiline("Email.ConfirmationCustomerButton", 200);
        AddMultiline("Email.ConfirmationSellerSubject", 200);
        AddMultiline("Email.ConfirmationSellerTitle", 200);
        AddMultiline("Email.ConfirmationSellerGreeting", 200);
        AddMultiline("Email.ConfirmationSellerButton", 200);
        AddMultiline("Email.ConfirmationLinkExpiry", 200);

        // ── HTTP / Timeouts ─────────────────────────────────────────
        AddInt("Http.MercadoPagoTimeoutSeconds", 1, 300, "s");
        AddInt("Http.ViaCepTimeoutSeconds", 1, 300, "s");
        AddInt("Http.AsaasTimeoutSeconds", 1, 300, "s");
        AddInt("Http.MercadoPagoRetryCount", 0, 10, "tentativas");
        AddInt("Http.MercadoPagoRetryDelayMs", 0, 60_000, "ms");
        AddInt("Http.MercadoPagoCircuitBreakerThreshold", 1, 100, "falhas");
        AddInt("Http.MercadoPagoCircuitBreakerSeconds", 1, 600, "s");
        AddInt("Http.ViaCepRetryCount", 0, 10, "tentativas");
        AddInt("Http.ViaCepRetryDelayMs", 0, 60_000, "ms");
        AddInt("Http.ViaCepCircuitBreakerThreshold", 1, 100, "falhas");
        AddInt("Http.ViaCepCircuitBreakerSeconds", 1, 600, "s");

        // ── Upload ──────────────────────────────────────────────────
        Add("Upload.AllowedExtensions", new(
            SystemParameterDisplayKind.Json,
            500,
            Pattern: @"^(\.[a-z0-9]+)(,\.[a-z0-9]+)*$",
            PatternMessage: "Liste extensões separadas por vírgula, cada uma iniciando com ponto (ex.: .jpg,.png)."));
        AddInt("Upload.MaxFileSizeBytes", 1, 104_857_600, "bytes");
        AddText("Upload.UrlPathPattern", 200);

        // ── Payment ─────────────────────────────────────────────────
        Add("Payment.Currency", new(
            SystemParameterDisplayKind.Text,
            3,
            Pattern: "^[A-Z]{3}$",
            PatternMessage: "Use um código ISO de moeda com 3 letras maiúsculas (ex.: BRL)."));
        AddText("Payment.MercadoPagoAutoReturn", 30);
        AddEmail("Payment.FallbackCustomerEmail");

        // ── Cookie ──────────────────────────────────────────────────
        AddText("Cookie.RefreshTokenName", 100, restricted: true);
        AddBoolean("Cookie.Secure", restricted: true);
        Add("Cookie.SameSiteMode", new(
            SystemParameterDisplayKind.Text,
            20,
            Restricted: true,
            Pattern: "^(Unspecified|None|Lax|Strict)$",
            PatternMessage: "Use Unspecified, None, Lax ou Strict."));
        AddText("Cookie.Path", 200, restricted: true);

        // ── Hangfire ────────────────────────────────────────────────
        AddInt("Hangfire.WorkerCount", 1, 64, "workers");
        Add("Hangfire.HeartbeatCron", new(
            SystemParameterDisplayKind.Cron,
            100,
            Pattern: @"^(\S+\s+){4,5}\S+$",
            PatternMessage: "Informe uma expressão cron com 5 ou 6 campos."));
        Add("Hangfire.SubscriptionNotificationCron", new(
            SystemParameterDisplayKind.Cron,
            100,
            Pattern: @"^(\S+\s+){4,5}\S+$",
            PatternMessage: "Informe uma expressão cron com 5 ou 6 campos."));

        // ── CORS ────────────────────────────────────────────────────
        AddBoolean("Cors.AllowAnyOrigin", restricted: true);

        return policies;
    }
}
