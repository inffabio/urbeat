namespace Urbeat.Infrastructure.Services;

public interface IAsaasSubscriptionAdapter
{
    Task<AsaasSubscriptionContractResponse> CreateContractAsync(
        AsaasSubscriptionContractRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AsaasSubscriptionContractRequest
{
    public Guid SellerUserId { get; init; }

    public string SellerName { get; init; } = string.Empty;

    public string SellerEmail { get; init; } = string.Empty;

    public string SellerPhone { get; init; } = string.Empty;

    public decimal PlanAmount { get; init; }

    public DateTime FirstDueDateUtc { get; init; }

    public string ExternalReference { get; init; } = string.Empty;
}

public sealed class AsaasSubscriptionContractResponse
{
    public string GatewayCustomerId { get; init; } = string.Empty;

    public string GatewaySubscriptionId { get; init; } = string.Empty;

    public DateTime NextDueDateUtc { get; init; }

    public string RawPayload { get; init; } = string.Empty;
}