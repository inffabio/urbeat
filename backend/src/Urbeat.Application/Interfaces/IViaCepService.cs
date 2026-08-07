using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IViaCepService
{
    Task<ViaCepAddressResponseDto?> LookupAsync(string cep, CancellationToken cancellationToken = default);
}
