namespace Urbeat.Domain.Entities;

/// <summary>
/// Configuração futura de integração de cobrança de um plano específico.
/// Nenhum valor sensível é exposto em APIs públicas ou logs; os campos
/// <c>Encrypted*</c> são preenchidos pelo serviço de criptografia e nunca
/// retornados em texto plano.
/// </summary>
public sealed class SubscriptionGatewayConfiguration : BaseEntity
{
    /// <summary>Plano ao qual esta configuração pertence (um registro por plano).</summary>
    public Guid PlanId { get; set; }

    public string Provider { get; set; } = "Asaas";

    public string Environment { get; set; } = "Sandbox";

    public string ApiBaseUrl { get; set; } = string.Empty;

    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// Referência ao segredo da API no Vault/secret store. Nunca contém o
    /// segredo em si, apenas o caminho/nome do item gerenciado externamente.
    /// </summary>
    public string ApiKeySecretReference { get; set; } = string.Empty;

    /// <summary>
    /// Referência ao segredo do webhook no Vault/secret store. Nunca contém o
    /// segredo em si.
    /// </summary>
    public string WebhookSecretReference { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }
}
