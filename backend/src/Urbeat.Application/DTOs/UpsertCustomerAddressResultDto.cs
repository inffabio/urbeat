namespace Urbeat.Application.DTOs;

public sealed class UpsertCustomerAddressResultDto
{
    public bool NotFound { get; init; }

    public bool LimitReached { get; init; }

    public CustomerAddressResponseDto? Address { get; init; }
}
