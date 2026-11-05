namespace Urbeat.Application.DTOs;

public sealed class ContractSellerSubscriptionRequestDto
{
    public Guid StoreId { get; init; }

    public Guid PlanId { get; init; }

    /// <summary>
    /// Mantido por compatibilidade de contrato, mas ignorado pelo servidor.
    /// A primeira cobrança é calculada a partir do trial do plano selecionado.
    /// </summary>
    public DateTime FirstDueDateUtc { get; init; }
}