using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Authoritative server-side flow for the one-shot administrative password recovery. The raw
/// recovery key is supplied only by the caller (transport header) and is never persisted.
/// </summary>
public interface IAdminPasswordRecoveryService
{
    /// <summary>
    /// True only when the feature is enabled and a usable key is configured. Controllers use this
    /// to answer as if the endpoint did not exist without echoing configuration details.
    /// </summary>
    bool IsAvailable { get; }

    Task<AdminPasswordRecoveryResultDto> RecoverAsync(
        AdminRecoverPasswordRequestDto request,
        string? providedRecoveryKey,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
