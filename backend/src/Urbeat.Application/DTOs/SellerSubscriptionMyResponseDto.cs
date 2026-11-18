using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class SellerSubscriptionMyResponseDto
{
    public bool HasSubscription { get; init; }

    /// <summary>
    /// Loja do lojista autenticado. Permite contratar o plano sem confiar em um
    /// StoreId enviado pela tela; o servidor sempre valida o ownership.
    /// </summary>
    public Guid? StoreId { get; init; }

    public Guid? PlanId { get; init; }

    public string? PlanName { get; init; }

    public string? PlanDescription { get; init; }

    public decimal? PlanAmount { get; init; }

    public SubscriptionBillingPeriod? BillingPeriod { get; init; }

    /// <summary>Nível do plano contratado; maior = superior.</summary>
    public int? PlanLevel { get; init; }

    /// <summary>Valor que sera cobrado quando o periodo gratuito terminar.</summary>
    public decimal? NextChargeAmount { get; init; }

    /// <summary>Dias de teste configurados no plano atual. Zero significa sem trial.</summary>
    public int? TrialDays { get; init; }

    /// <summary>Fim do periodo gratuito calculado pelo servidor. Nulo quando o plano nao tem trial.</summary>
    public DateTime? TrialEndsAtUtc { get; init; }

    /// <summary>Verdadeiro somente entre o inicio da assinatura e o fim do trial, sem contrato de gateway.</summary>
    public bool IsTrialActive { get; init; }

    /// <summary>Verdadeiro quando a assinatura ja foi contratada junto ao gateway.</summary>
    public bool HasGatewaySubscription { get; init; }

    /// <summary>Existe cobrança stub aguardando integração de gateway.</summary>
    public bool HasPendingGatewayCharge { get; init; }

    public decimal? PendingChargeAmount { get; init; }

    public DateTime? PendingChargeDueDateUtc { get; init; }

    /// <summary>Texto honesto exibido ao lojista enquanto o boleto real não é gerado.</summary>
    public string PendingChargeStatus { get; init; } = "Boleto aguardando integração";

    public SellerSubscriptionBillingStatus? BillingStatus { get; init; }

    public DateTime? NextDueDateUtc { get; init; }

    public string LastChargeStatus { get; init; } = "Nao contratado";

    public bool StoreBlocked { get; init; }

    public string RegularizationMessage { get; init; } = string.Empty;
}
