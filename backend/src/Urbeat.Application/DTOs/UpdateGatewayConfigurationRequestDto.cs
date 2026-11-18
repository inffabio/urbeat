namespace Urbeat.Application.DTOs;

/// <summary>
/// Configuração futura do gateway de cobrança da assinatura. Os segredos são
/// opcionais: ausente preserva o valor atual; string vazia remove o segredo.
/// </summary>
public sealed class UpdateGatewayConfigurationRequestDto
{
    public string Provider { get; init; } = "Asaas";

    public string Environment { get; init; } = "Sandbox";

    public string ApiBaseUrl { get; init; } = string.Empty;

    public string WebhookUrl { get; init; } = string.Empty;

    /// <summary>
    /// Referência ao segredo no Vault/secret store. Ausente preserva a atual;
    /// string vazia remove a referência. O segredo em si nunca trafega aqui.
    /// </summary>
    public string? ApiKeySecretReference { get; init; }

    /// <summary>
    /// Referência ao segredo do webhook no Vault/secret store. Ausente preserva
    /// a atual; string vazia remove a referência.
    /// </summary>
    public string? WebhookSecretReference { get; init; }

    public bool IsEnabled { get; init; }
}
