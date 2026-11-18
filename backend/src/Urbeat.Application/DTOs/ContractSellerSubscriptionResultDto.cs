namespace Urbeat.Application.DTOs;

public sealed class ContractSellerSubscriptionResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidPlan { get; init; }

    /// <summary>O plano não oferece a periodicidade solicitada.</summary>
    public bool InvalidBillingPeriod { get; init; }

    /// <summary>Já existe assinatura contratada e o plano não é superior.</summary>
    public bool DowngradeNotAllowed { get; init; }

    public bool AlreadyContracted { get; init; }

    public ContractSellerSubscriptionResponseDto? Subscription { get; init; }
}
