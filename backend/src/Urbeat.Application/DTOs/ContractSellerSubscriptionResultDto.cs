namespace Urbeat.Application.DTOs;

public sealed class ContractSellerSubscriptionResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidPlan { get; init; }

    public bool AlreadyContracted { get; init; }

    public ContractSellerSubscriptionResponseDto? Subscription { get; init; }
}