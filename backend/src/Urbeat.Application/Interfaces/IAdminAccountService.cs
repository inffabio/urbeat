using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Interfaces;

public enum AdminInvitationClaimStatus
{
    Valid,
    Invalid,
    Expired,
    AlreadyConsumed,
    Revoked,
    Superseded
}

public sealed class AdminInvitationClaimResult
{
    public AdminInvitationClaimStatus Status { get; init; }

    public AdminInvitation? Invitation { get; init; }

    public Guid? ClaimAttemptId { get; init; }

    public string Token { get; init; } = string.Empty;

    public bool IsValid => Status == AdminInvitationClaimStatus.Valid;
}

public sealed class AdminInvitationEmailDelivery
{
    public Guid InvitationId { get; init; }

    public Guid AttemptId { get; init; }

    public string Email { get; init; } = string.Empty;

    public AdminInvitationPurpose Purpose { get; init; }

    public string Token { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }
}

/// <summary>
/// Persistence and cryptographic operations for administrative invitation links. Tokens are derived
/// (never stored) and only their hash is persisted.
/// </summary>
public interface IAdminInvitationService
{
    string CreateToken(Guid invitationId, AdminInvitationPurpose purpose);

    Task<AdminInvitation> CreateAsync(
        Guid? userId,
        string email,
        AdminInvitationPurpose purpose,
        Guid? createdByAdminId,
        CancellationToken cancellationToken = default);

    Task<AdminInvitation?> FindByIdAsync(Guid invitationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminInvitation>> FindActiveAsync(
        Guid userId,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminInvitation>> FindByHashAsync(
        string token,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken = default);

    Task<AdminInvitationClaimResult> TryClaimAsync(
        string token,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken = default);

    Task<bool> MarkConsumedAsync(
        Guid invitationId,
        Guid claimAttemptId,
        CancellationToken cancellationToken = default);

    Task ReleaseClaimAsync(
        Guid invitationId,
        Guid claimAttemptId,
        CancellationToken cancellationToken = default);

    Task<AdminInvitationEmailDelivery?> TryClaimEmailDeliveryAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default);

    Task MarkEmailSentAsync(
        Guid invitationId,
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task ReleaseEmailSendLeaseAsync(
        Guid invitationId,
        Guid attemptId,
        CancellationToken cancellationToken = default);

    AdminInvitationState ResolveState(AdminInvitation invitation, DateTime utcNow);

    AdminInvitationDto ToDto(AdminInvitation invitation, DateTime utcNow);
}

/// <summary>
/// Authoritative administrative account management. Every method returns a stable status so the
/// controller can translate it to the documented HTTP contract without leaking secrets.
/// </summary>
public interface IAdminAccountService
{
    Task<PagedAdminAdministratorsDto> ListAsync(
        AdminAccountsQueryDto query,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<AdminAccountOperationResult> InviteAsync(
        string email,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AdminAccountOperationResult> UpdateEmailAsync(
        Guid targetUserId,
        string email,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AdminAccountInvitationDeliveryResult> ResendInvitationAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AdminAccountOperationResult> CancelInvitationAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AdminAccountInvitationDeliveryResult> RequestPasswordResetAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AdminAccountOperationResult> BlockAsync(
        Guid targetUserId,
        string? reason,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AdminAccountOperationResult> UnblockAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AdminAccountOperationResult> RemoveAsync(
        Guid targetUserId,
        string? reason,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AdminInvitationValidationDto> ValidateInvitationTokenAsync(
        string token,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken = default);

    Task<AdminAccountSetupResultDto> AcceptInvitationAsync(
        string token,
        string newPassword,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<AdminAccountSetupResultDto> ConfirmPasswordResetAsync(
        string token,
        string newPassword,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
