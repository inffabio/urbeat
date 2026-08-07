using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ICustomerAddressService
{
    Task<IReadOnlyCollection<CustomerAddressResponseDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UpsertCustomerAddressResultDto> CreateAsync(
        Guid userId,
        UpsertCustomerAddressRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<UpsertCustomerAddressResultDto> UpdateAsync(
        Guid userId,
        Guid addressId,
        UpsertCustomerAddressRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid userId, Guid addressId, string? ipAddress, CancellationToken cancellationToken = default);
}
