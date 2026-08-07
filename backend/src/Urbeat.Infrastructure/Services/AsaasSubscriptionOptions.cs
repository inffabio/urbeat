namespace Urbeat.Infrastructure.Services;

public sealed class AsaasSubscriptionOptions
{
    public const string SectionName = "AsaasSubscription";

    public string BaseUrl { get; init; } = "https://api.asaas.com";

    public string ApiKey { get; init; } = string.Empty;
}