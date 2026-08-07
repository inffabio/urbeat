namespace Urbeat.Application.DTOs;

public sealed class UpdateStoreResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool SubscriptionBlocked { get; init; }

    public bool InvalidCuisineType { get; init; }

    public StoreResponseDto? Store { get; init; }
}