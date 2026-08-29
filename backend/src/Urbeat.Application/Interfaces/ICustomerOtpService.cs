using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ICustomerOtpService
{
    Task<StartCustomerVerificationResponseDto> StartAsync(StartCustomerVerificationRequestDto request, CancellationToken cancellationToken = default);

    Task<CustomerSessionResultDto> CreateCustomerSessionAsync(StartCustomerVerificationRequestDto request, CancellationToken cancellationToken = default);

    Task<CustomerSessionResultDto> ConfirmAsync(ConfirmCustomerVerificationRequestDto request, CancellationToken cancellationToken = default);

    Task<ResendCustomerVerificationResponseDto> ResendAsync(ResendCustomerVerificationRequestDto request, CancellationToken cancellationToken = default);
}
