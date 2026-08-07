namespace Urbeat.Application.DTOs;

public sealed class UpsertStoreAddressResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public StoreAddressResponseDto? Address { get; init; }
}