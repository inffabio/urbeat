namespace Urbeat.Application.DTOs;

/// <summary>
/// Visão administrativa da configuração de gateway. Nunca inclui valores de
/// segredo: apenas indica se um segredo já está configurado.
/// </summary>
public sealed class GatewayConfigurationResponseDto
{
    public Guid Id { get; init; }

    public Guid PlanId { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string Environment { get; init; } = string.Empty;

    public string ApiBaseUrl { get; init; } = string.Empty;

    public string WebhookUrl { get; init; } = string.Empty;

    public bool HasApiKey { get; init; }

    public bool HasWebhookSecret { get; init; }

    public bool IsEnabled { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }
}
