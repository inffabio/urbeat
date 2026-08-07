using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ICheckoutService
{
    Task<CheckoutResultDto> PreviewAsync(
        Guid? customerUserId,
        CheckoutRequestDto request,
        CancellationToken cancellationToken = default);

    Task<CheckoutResultDto> ConfirmAsync(
        Guid customerUserId,
        CheckoutRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
