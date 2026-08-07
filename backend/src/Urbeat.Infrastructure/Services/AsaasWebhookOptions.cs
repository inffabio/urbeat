namespace Urbeat.Infrastructure.Services;

public sealed class AsaasWebhookOptions
{
    public const string SectionName = "AsaasWebhook";

    public string Token { get; init; } = string.Empty;
}
