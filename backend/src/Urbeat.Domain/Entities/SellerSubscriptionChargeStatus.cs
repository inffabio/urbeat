namespace Urbeat.Domain.Entities;

/// <summary>
/// Estados da cobrança local de assinatura. Nesta versão existe apenas o stub
/// aguardando a futura integração de gateway: nenhum boleto, PDF, linha
/// digitável ou pagamento é gerado.
/// </summary>
public enum SellerSubscriptionChargeStatus
{
    AwaitingGateway = 1
}
