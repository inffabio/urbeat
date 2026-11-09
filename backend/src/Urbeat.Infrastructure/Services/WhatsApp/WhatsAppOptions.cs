namespace Urbeat.Infrastructure.Services.WhatsApp;

/// <summary>
/// WhatsApp Cloud API configuration. Secret values are supplied by the deployment environment
/// (Vault-backed environment variables) and are never written to logs or returned to clients.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>
    /// Single source of truth for the Graph API version used by every WhatsApp call. Keep it in a
    /// version still supported by Meta (see the Graph API changelog) and override it through
    /// <c>WhatsApp__GraphApiVersion</c> when Meta publishes a newer version.
    /// </summary>
    public const string DefaultGraphApiVersion = "v26.0";

    /// <summary>
    /// Explicit opt-in for the WhatsApp integration. When <c>false</c> (the default) startup does
    /// not require any WhatsApp secret: the webhook endpoints stay fail-closed (403/401) and
    /// outbound sends return <c>not_configured</c> instead of taking the whole API down. Set it to
    /// <c>true</c> only when the four Vault-backed values are actually available.
    /// </summary>
    public bool WebhookEnabled { get; init; }

    /// <summary>Meta webhook verification token (Vault display name: WhatsAppWebhooksToken).</summary>
    public string WebhookVerifyToken { get; init; } = string.Empty;

    /// <summary>Meta App Secret used to validate X-Hub-Signature-256 (Vault: MetaChaveApp).</summary>
    public string AppSecret { get; init; } = string.Empty;

    /// <summary>WhatsApp Business Account id (Vault: MetaWhatsappWABA).</summary>
    public string WabaId { get; init; } = string.Empty;

    /// <summary>Cloud API access token (Vault: MetaTokenWhatsapp).</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// Public Meta App ID (not a secret) used only by the Resumable Upload API when refreshing the
    /// business profile picture. Empty means the profile operation reports <c>not_configured</c>.
    /// </summary>
    public string MetaAppId { get; init; } = string.Empty;

    /// <summary>
    /// Public HTTPS URL of the institutional urbeat logo applied to the WhatsApp business profile.
    /// The profile picture is global for the Phone Number ID; it is never per store.
    /// </summary>
    public string ProfilePictureUrl { get; init; } = "https://urbeat.com.br/assets/images/urbeat-logo.jpg";

    public string PhoneNumberId { get; init; } = string.Empty;

    public string GraphApiVersion { get; init; } = DefaultGraphApiVersion;

    public string GraphApiBaseUrl { get; init; } = "https://graph.facebook.com";

    public int TimeoutSeconds { get; init; } = 15;

    public bool HasSendCredentials =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);

    public bool HasProfileUpdateCredentials =>
        HasSendCredentials && !string.IsNullOrWhiteSpace(MetaAppId);
}
